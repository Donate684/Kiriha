using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Repository;

public sealed class SqliteScrobbleProgressStore : IScrobbleProgressStore
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly string? _legacyJsonPath;
    private readonly DeferredKeyedState<int> _state;

    public SqliteScrobbleProgressStore(
        IDbContextFactory<AppDbContext> contextFactory,
        string? legacyJsonPath = null)
    {
        _contextFactory = contextFactory;
        _legacyJsonPath = legacyJsonPath;
        _state = new(() => Task.Run(LoadAsync).GetAwaiter().GetResult(), PersistAsync,
            ex => Log.Warning(ex, "Failed to load or persist SQLite scrobble checkpoints"));
    }

    public int GetElapsed(string key) =>
        !string.IsNullOrWhiteSpace(key) && _state.TryGet(key, out var elapsed) ? elapsed : 0;

    public void SaveElapsed(string key, int elapsed)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _state.Set(key, Math.Max(0, elapsed));
    }

    public void Clear(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _state.Remove(key);
    }

    public Task FlushAsync(CancellationToken ct = default) => _state.FlushAsync(ct);

    private async Task<Dictionary<string, int>> LoadAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Database.SetCommandTimeout(1);
        var progress = await context.ScrobbleProgress.ToDictionaryAsync(
            row => row.Key, row => Math.Max(0, row.ElapsedSeconds), StringComparer.Ordinal);

        if (_legacyJsonPath is not { Length: > 0 } path || !File.Exists(path))
            return progress;

        Dictionary<string, int>? legacy;
        try
        {
            await using var input = File.OpenRead(path);
            legacy = await JsonSerializer.DeserializeAsync<Dictionary<string, int>>(input);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Invalid legacy scrobble checkpoints in {Path}", path);
            return progress;
        }

        foreach (var (key, elapsed) in legacy ?? [])
        {
            if (string.IsNullOrWhiteSpace(key) || elapsed <= 0 || !progress.TryAdd(key, elapsed))
                continue;

            context.ScrobbleProgress.Add(new ScrobbleProgressEntity
            {
                Key = key,
                ElapsedSeconds = elapsed,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        File.Delete(path);
        return progress;
    }

    private async Task PersistAsync(string key, int? elapsed)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Database.SetCommandTimeout(1);
        if (elapsed is { } value)
        {
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO scrobble_progress (key, elapsed_seconds, updated_at)
                VALUES ({key}, {value}, {DateTime.UtcNow})
                ON CONFLICT (key) DO UPDATE SET
                    elapsed_seconds = excluded.elapsed_seconds,
                    updated_at = excluded.updated_at
                """);
        }
        else
        {
            await context.ScrobbleProgress.Where(row => row.Key == key).ExecuteDeleteAsync();
        }
    }
}
