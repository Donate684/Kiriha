using System.Collections.Concurrent;
using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Repository;

public sealed class SqliteScrobbleProgressStore : IScrobbleProgressStore
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly string? _legacyJsonPath;
    private readonly ConcurrentDictionary<string, int> _progress = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> _pendingWrites = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _initLock = new();
    private bool _initialized;

    public SqliteScrobbleProgressStore(
        IDbContextFactory<AppDbContext> contextFactory,
        string? legacyJsonPath = null)
    {
        _contextFactory = contextFactory;
        _legacyJsonPath = legacyJsonPath;
    }

    public int GetElapsed(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        EnsureInitialized();
        return _progress.TryGetValue(key, out var elapsed) ? Math.Max(0, elapsed) : 0;
    }

    public void SaveElapsed(string key, int elapsed)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        EnsureInitialized();
        var clamped = Math.Max(0, elapsed);
        _progress[key] = clamped;
        TrackWrite(UpsertInternalAsync(key, clamped));
    }

    public void Clear(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        EnsureInitialized();
        if (_progress.TryRemove(key, out _))
        {
            TrackWrite(DeleteInternalAsync(key));
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        var pending = _pendingWrites.Keys.ToArray();
        if (pending.Length == 0)
            return;

        try
        {
            await Task.WhenAll(pending).WaitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "SqliteScrobbleProgressStore: error while flushing pending writes");
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            try
            {
                using var context = _contextFactory.CreateDbContext();
                var rows = context.ScrobbleProgress.AsNoTracking().ToList();
                foreach (var row in rows)
                {
                    _progress[row.Key] = Math.Max(0, row.ElapsedSeconds);
                }

                MigrateLegacyJsonIfNeeded(context);
                _initialized = true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SqliteScrobbleProgressStore: failed initial hydration from SQLite");
            }
        }
    }

    private void MigrateLegacyJsonIfNeeded(AppDbContext context)
    {
        if (string.IsNullOrWhiteSpace(_legacyJsonPath) || !File.Exists(_legacyJsonPath))
            return;

        try
        {
            var json = File.ReadAllText(_legacyJsonPath);
            var legacy = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (legacy is { Count: > 0 })
            {
                var now = DateTime.UtcNow;
                bool addedAny = false;
                foreach (var (key, val) in legacy)
                {
                    if (string.IsNullOrWhiteSpace(key) || val <= 0)
                        continue;

                    if (_progress.TryAdd(key, val))
                    {
                        context.ScrobbleProgress.Add(new ScrobbleProgressEntity
                        {
                            Key = key,
                            ElapsedSeconds = val,
                            UpdatedAt = now
                        });
                        addedAny = true;
                    }
                }

                if (addedAny)
                {
                    context.SaveChanges();
                }
            }

            File.Delete(_legacyJsonPath);
            Log.Information("SqliteScrobbleProgressStore: migrated and deleted legacy {Path}", _legacyJsonPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SqliteScrobbleProgressStore: failed migrating legacy {Path}", _legacyJsonPath);
        }
    }

    private async Task UpsertInternalAsync(string key, int elapsed)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.ScrobbleProgress.AsTracking().FirstOrDefaultAsync(x => x.Key == key);
            if (existing is null)
            {
                context.ScrobbleProgress.Add(new ScrobbleProgressEntity
                {
                    Key = key,
                    ElapsedSeconds = elapsed,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.ElapsedSeconds = elapsed;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SqliteScrobbleProgressStore: failed to persist key {Key}", key);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task DeleteInternalAsync(string key)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            await context.ScrobbleProgress.Where(x => x.Key == key).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SqliteScrobbleProgressStore: failed to delete key {Key}", key);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void TrackWrite(Task task)
    {
        if (task.IsCompleted)
            return;

        _pendingWrites.TryAdd(task, 0);
        task.ContinueWith(
            t => _pendingWrites.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
