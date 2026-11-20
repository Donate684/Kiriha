using System.Collections.Concurrent;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public class HistoryService : IHistoryService
{
    private readonly IHistoryRepository _repo;
    // Tracks in-flight fire-and-forget writes so FlushAsync awaits all of them before shutdown.
    private readonly ConcurrentDictionary<Task, byte> _pendingWrites = new();

    public event Action<HistoryItem>? EntryAdded;
    public event Action<int, MediaKind, int?, string, TrackerSyncState, string?>? TrackerStatusUpdated;

    public HistoryService(IHistoryRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<HistoryItem>> GetHistoryAsync(int limit = 1000, CancellationToken ct = default)
    {
        try
        {
            return await _repo.GetAsync(limit, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get history from database");
            return [];
        }
    }

    public virtual async Task AddEntryAsync(
        int animeId,
        MediaKind mediaKind,
        string title,
        string? russianTitle,
        int episode,
        HistoryActionType actionType,
        object? detail = null,
        string? posterUrl = null,
        CancellationToken ct = default)
    {
        try
        {
            var entry = new HistoryItem
            {
                AnimeId = animeId,
                MediaKind = mediaKind,
                AnimeTitle = title,
                RussianTitle = russianTitle,
                Episode = episode,
                Timestamp = DateTime.UtcNow,
                ActionType = (int)actionType,
                Detail = detail?.ToString() ?? string.Empty,
                PosterUrl = posterUrl
            };

            await _repo.AddAsync(entry, ct);
            PublishCommittedEntry(entry);
            Log.Debug("History entry added for {Id} ({MediaKind}) {Title} (Ep {Ep})", animeId, mediaKind, title, episode);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add history entry for {Title}", title);
        }
    }

    public void PublishCommittedEntry(HistoryItem entry)
    {
        try
        {
            EntryAdded?.Invoke(entry);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to publish committed history entry {HistoryId}", entry.Id);
        }
    }

    public virtual void AddEntry(
        int animeId,
        MediaKind mediaKind,
        string title,
        string? russianTitle,
        int episode,
        HistoryActionType actionType,
        object? detail = null,
        string? posterUrl = null)
    {
        TrackPendingWrite(AddEntryAsync(animeId, mediaKind, title, russianTitle, episode, actionType, detail, posterUrl));
    }

    public virtual void UpdateTrackerStatus(
        int animeId,
        MediaKind mediaKind,
        int? episode,
        string trackerName,
        TrackerSyncState state,
        string? error = null)
    {
        TrackPendingWrite(UpdateTrackerStatusAsync(animeId, mediaKind, episode, trackerName, state, error));
    }

    public virtual async Task UpdateTrackerStatusAsync(
        int animeId,
        MediaKind mediaKind,
        int? episode,
        string trackerName,
        TrackerSyncState state,
        string? error = null,
        CancellationToken ct = default)
    {
        try
        {
            await _repo.UpdateTrackerStatusAsync(animeId, mediaKind, episode, trackerName, state, error, ct);
            TrackerStatusUpdated?.Invoke(animeId, mediaKind, episode, trackerName, state, error);
            Log.Debug("HistoryService: Updated tracker status for {Id} ({MediaKind}, Ep {Ep}) {Tracker}: {State}", animeId, mediaKind, episode, trackerName, state);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HistoryService: Failed to update tracker status for {AnimeId} on {Tracker}", animeId, trackerName);
        }
    }

    public virtual void SetPendingTrackers(
        int animeId,
        MediaKind mediaKind,
        int? episode,
        IEnumerable<string> trackerNames)
    {
        TrackPendingWrite(SetPendingTrackersAsync(animeId, mediaKind, episode, trackerNames));
    }

    public virtual async Task SetPendingTrackersAsync(
        int animeId,
        MediaKind mediaKind,
        int? episode,
        IEnumerable<string> trackerNames,
        CancellationToken ct = default)
    {
        try
        {
            var trackers = trackerNames.ToList();
            await _repo.SetPendingTrackersAsync(animeId, mediaKind, episode, trackers, ct);

            foreach (var tracker in trackers)
            {
                TrackerStatusUpdated?.Invoke(animeId, mediaKind, episode, tracker, TrackerSyncState.Pending, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HistoryService: Failed to set pending trackers for {AnimeId}", animeId);
        }
    }

    private void TrackPendingWrite(Task task)
    {
        if (task.IsCompleted) return;

        _pendingWrites.TryAdd(task, 0);
        task.ContinueWith(t => _pendingWrites.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Awaits every fire-and-forget write that hasn't yet committed to the DB.
    /// Call this on application shutdown BEFORE DatabaseInitializer.FlushAsync so the WAL
    /// checkpoint sees the final scrobble/ScoreSet/Reverted entries.
    /// </summary>
    public async Task FlushAsync(TimeSpan? timeout = null)
    {
        var pending = _pendingWrites.Keys.ToArray();
        if (pending.Length == 0) return;

        try
        {
            var all = Task.WhenAll(pending);
            if (timeout.HasValue)
            {
                var done = await Task.WhenAny(all, Task.Delay(timeout.Value));
                if (done != all)
                {
                    Log.Warning("HistoryService: FlushAsync timed out with {Count} pending writes", pending.Length);
                    return;
                }
            }
            else
            {
                await all;
            }
            Log.Debug("HistoryService: Flushed {Count} pending writes", pending.Length);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HistoryService: FlushAsync observed an exception in a pending write");
        }
    }
}
