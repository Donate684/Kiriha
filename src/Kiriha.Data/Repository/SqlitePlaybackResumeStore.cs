using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Repository;

public sealed class SqlitePlaybackResumeStore : IPlaybackResumeStore
{
    private readonly record struct ResumeEntry(double Position, double Duration, DateTime UpdatedAt);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly string? _legacyJsonPath;
    private readonly DeferredKeyedState<ResumeEntry> _state;

    public SqlitePlaybackResumeStore(
        IDbContextFactory<AppDbContext> contextFactory,
        string? legacyJsonPath = null)
    {
        _contextFactory = contextFactory;
        _legacyJsonPath = legacyJsonPath;
        _state = new(Load, PersistAsync, ex => Log.Warning(ex, "Failed to load or persist SQLite playback positions"));
    }

    public void SavePosition(string url, double position, double duration)
    {
        if (!string.IsNullOrWhiteSpace(url) && IsValid(position, duration))
            _state.Set(url, new ResumeEntry(position, duration, DateTime.UtcNow));
    }

    public double? TryGetResumePosition(string url, string? watchLaterDir = null)
    {
        if (!string.IsNullOrWhiteSpace(url) && _state.TryGet(url, out var entry) &&
            IsValid(entry.Position, entry.Duration) &&
            (entry.Duration <= 0 || entry.Position < entry.Duration * 0.95))
        {
            return entry.Position;
        }

        return null;
    }

    public void Remove(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
            _state.Remove(url);
    }

    public Task FlushAsync(CancellationToken ct = default) => _state.FlushAsync(ct);

    private static bool IsValid(double position, double duration) =>
        double.IsFinite(position) && position >= 5 && double.IsFinite(duration);

    private Dictionary<string, ResumeEntry> Load()
    {
        // Keep the legacy read, database commit, and file deletion under the standalone player's lock.
        // A named mutex must be released by the same thread that acquired it.
        using var fileLock = _legacyJsonPath is { Length: > 0 } path ? StateFileLock.Acquire(path) : null;
        return Task.Run(LoadAsync).GetAwaiter().GetResult();
    }

    private async Task<Dictionary<string, ResumeEntry>> LoadAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Database.SetCommandTimeout(1);
        var entries = await context.PlaybackResume.ToDictionaryAsync(
            row => row.VideoUrl, row => new ResumeEntry(row.PositionSeconds, row.DurationSeconds, row.UpdatedAt),
            StringComparer.Ordinal);

        if (_legacyJsonPath is not { Length: > 0 } path || !File.Exists(path))
            return entries;

        Dictionary<string, ResumeEntry>? legacy;
        try
        {
            await using var input = File.OpenRead(path);
            legacy = await JsonSerializer.DeserializeAsync<Dictionary<string, ResumeEntry>>(input, JsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Invalid legacy playback positions in {Path}", path);
            return entries;
        }

        foreach (var (url, entry) in legacy ?? [])
        {
            var imported = entry.UpdatedAt == default ? entry with { UpdatedAt = DateTime.UtcNow } : entry;
            if (string.IsNullOrWhiteSpace(url) || !IsValid(entry.Position, entry.Duration) || !entries.TryAdd(url, imported))
                continue;

            context.PlaybackResume.Add(new PlaybackResumeEntryEntity
            {
                VideoUrl = url,
                PositionSeconds = imported.Position,
                DurationSeconds = imported.Duration,
                UpdatedAt = imported.UpdatedAt
            });
        }

        await context.SaveChangesAsync();
        File.Delete(path);
        return entries;
    }

    private async Task PersistAsync(string url, ResumeEntry? entry)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Database.SetCommandTimeout(1);
        if (entry is { } value)
        {
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO playback_resume (video_url, position_seconds, duration_seconds, updated_at)
                VALUES ({url}, {value.Position}, {value.Duration}, {value.UpdatedAt})
                ON CONFLICT (video_url) DO UPDATE SET
                    position_seconds = excluded.position_seconds,
                    duration_seconds = excluded.duration_seconds,
                    updated_at = excluded.updated_at
                """);
        }
        else
        {
            await context.PlaybackResume.Where(row => row.VideoUrl == url).ExecuteDeleteAsync();
        }
    }
}
