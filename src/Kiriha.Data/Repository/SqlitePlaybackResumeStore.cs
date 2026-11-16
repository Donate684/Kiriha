using System.Collections.Concurrent;
using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Repository;

public sealed class SqlitePlaybackResumeStore : IPlaybackResumeStore
{
    private readonly record struct LegacyResumeDto(double Position, double Duration, DateTime UpdatedAt);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly string? _legacyJsonPath;
    private readonly ConcurrentDictionary<string, PlaybackResumeEntryEntity> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> _pendingWrites = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _initLock = new();
    private bool _initialized;

    public SqlitePlaybackResumeStore(
        IDbContextFactory<AppDbContext> contextFactory,
        string? legacyJsonPath = null)
    {
        _contextFactory = contextFactory;
        _legacyJsonPath = legacyJsonPath;
    }

    public void SavePosition(string url, double position, double duration)
    {
        if (string.IsNullOrWhiteSpace(url) || position < 5)
            return;

        EnsureInitialized();
        var entry = new PlaybackResumeEntryEntity
        {
            VideoUrl = url,
            PositionSeconds = position,
            DurationSeconds = duration,
            UpdatedAt = DateTime.UtcNow
        };
        _entries[url] = entry;
        TrackWrite(UpsertInternalAsync(entry));
    }

    public double? TryGetResumePosition(string url, string? watchLaterDir = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        EnsureInitialized();
        if (_entries.TryGetValue(url, out var entry))
        {
            if (entry.PositionSeconds >= 5 &&
                (entry.DurationSeconds <= 0 || entry.PositionSeconds < entry.DurationSeconds * 0.95))
            {
                return entry.PositionSeconds;
            }
        }

        return null;
    }

    public void Remove(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        EnsureInitialized();
        if (_entries.TryRemove(url, out _))
        {
            TrackWrite(DeleteInternalAsync(url));
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
            Log.Warning(ex, "SqlitePlaybackResumeStore: error while flushing pending writes");
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
                var rows = context.PlaybackResume.AsNoTracking().ToList();
                foreach (var row in rows)
                {
                    _entries[row.VideoUrl] = row;
                }

                MigrateLegacyJsonIfNeeded(context);
                _initialized = true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SqlitePlaybackResumeStore: failed initial hydration from SQLite");
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
            var legacy = JsonSerializer.Deserialize<Dictionary<string, LegacyResumeDto>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (legacy is { Count: > 0 })
            {
                bool addedAny = false;
                foreach (var (url, dto) in legacy)
                {
                    if (string.IsNullOrWhiteSpace(url) || dto.Position < 5)
                        continue;

                    var entity = new PlaybackResumeEntryEntity
                    {
                        VideoUrl = url,
                        PositionSeconds = dto.Position,
                        DurationSeconds = dto.Duration,
                        UpdatedAt = dto.UpdatedAt == default ? DateTime.UtcNow : dto.UpdatedAt
                    };

                    if (_entries.TryAdd(url, entity))
                    {
                        context.PlaybackResume.Add(entity);
                        addedAny = true;
                    }
                }

                if (addedAny)
                {
                    context.SaveChanges();
                }
            }

            File.Delete(_legacyJsonPath);
            Log.Information("SqlitePlaybackResumeStore: migrated and deleted legacy {Path}", _legacyJsonPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SqlitePlaybackResumeStore: failed migrating legacy {Path}", _legacyJsonPath);
        }
    }

    private async Task UpsertInternalAsync(PlaybackResumeEntryEntity entry)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.PlaybackResume.AsTracking().FirstOrDefaultAsync(x => x.VideoUrl == entry.VideoUrl);
            if (existing is null)
                context.PlaybackResume.Add(entry);
            else
                context.Entry(existing).CurrentValues.SetValues(entry);

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SqlitePlaybackResumeStore: failed to persist resume position");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task DeleteInternalAsync(string url)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            await context.PlaybackResume.Where(x => x.VideoUrl == url).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SqlitePlaybackResumeStore: failed to delete resume position");
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
