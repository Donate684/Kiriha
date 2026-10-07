using System.Collections.Concurrent;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Services.Data.Core;

public class HistoryService : IHistoryService
{
    private readonly IHistoryRepository _repo;
    // Tracks in-flight AddEntryAsync calls launched via the fire-and-forget AddEntry overload.
    // FlushAsync awaits all of them so a shutdown can't drop the last few scrobble records.
    private readonly ConcurrentDictionary<Task, byte> _pendingWrites = new();

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

    public virtual Task AddEntryAsync(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null, CancellationToken ct = default)
        => AddEntryAsync(animeId, title, russianTitle, episode, actionType, detail, posterUrl: null, ct);

    public virtual async Task AddEntryAsync(int animeId, string title, string? russianTitle, int episode, string actionType, object? detail, string? posterUrl, CancellationToken ct = default)
    {
        try
        {
            int typeId = actionType switch
            {
                "Watched" or "Watching" or "Read" or "Reading" => 1,
                "Reverted" => 2,
                "SyncFailed" => 3,
                "Scrobbled" => 4,
                "ScoreSet" => 5,
                "Completed" => 6,
                "Dropped" => 7,
                "Deleted" or "Removed" => 8,
                "PlanToWatch" or "Plan_To_Watch" => 9,
                "OnHold" or "On_Hold" => 10,
                "Rewatching" => 11,
                "AddedToList" or "Added" => 12,
                _ => 1
            };

            var entry = new HistoryItem
            {
                AnimeId = animeId,
                AnimeTitle = title,
                RussianTitle = russianTitle,
                Episode = episode,
                Timestamp = DateTime.UtcNow,
                ActionType = typeId,
                Detail = detail?.ToString() ?? "",
                PosterUrl = posterUrl
            };

            await _repo.AddAsync(entry, ct);
            EntryAdded?.Invoke(entry);
            Log.Debug("History entry added for {Id} {Title} (Ep {Ep})", animeId, title, episode);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add history entry for {Title}", title);
        }
    }

    public virtual void AddEntry(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null)
        => AddEntry(animeId, title, russianTitle, episode, actionType, detail, posterUrl: null);

    public virtual void AddEntry(int animeId, string title, string? russianTitle, int episode, string actionType, object? detail, string? posterUrl)
    {
        var task = AddEntryAsync(animeId, title, russianTitle, episode, actionType, detail, posterUrl);
        if (task.IsCompleted) return; // Synchronous fast-path: nothing to track.

        _pendingWrites.TryAdd(task, 0);
        // Best-effort cleanup so the dictionary doesn't grow unbounded across the
        // session. The continuation runs on the threadpool when SaveChanges resolves.
        task.ContinueWith(t => _pendingWrites.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public virtual void UpdateTrackerStatus(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error = null)
    {
        var task = UpdateTrackerStatusAsync(animeId, episode, trackerName, state, error);
        if (task.IsCompleted) return;

        _pendingWrites.TryAdd(task, 0);
        task.ContinueWith(t => _pendingWrites.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public event Action<HistoryItem>? EntryAdded;
    public event Action<int, int?, string, TrackerSyncState, string?>? TrackerStatusUpdated;

    public virtual async Task UpdateTrackerStatusAsync(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error = null, CancellationToken ct = default)
    {
        try
        {
            await _repo.UpdateTrackerStatusAsync(animeId, episode, trackerName, state, error, ct);
            TrackerStatusUpdated?.Invoke(animeId, episode, trackerName, state, error);
            Log.Debug("HistoryService: Updated tracker status for {Id} (Ep {Ep}) {Tracker}: {State}", animeId, episode, trackerName, state);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HistoryService: Failed to update tracker status for {AnimeId} on {Tracker}", animeId, trackerName);
        }
    }

    public virtual void SetPendingTrackers(int animeId, int? episode, IEnumerable<string> trackerNames)
    {
        var task = SetPendingTrackersAsync(animeId, episode, trackerNames);
        if (task.IsCompleted) return;

        _pendingWrites.TryAdd(task, 0);
        task.ContinueWith(t => _pendingWrites.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public virtual async Task SetPendingTrackersAsync(int animeId, int? episode, IEnumerable<string> trackerNames, CancellationToken ct = default)
    {
        try
        {
            var trackers = trackerNames.ToList();
            await _repo.SetPendingTrackersAsync(animeId, episode, trackers, ct);
            foreach (var tracker in trackers)
            {
                TrackerStatusUpdated?.Invoke(animeId, episode, tracker, TrackerSyncState.Pending, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HistoryService: Failed to set pending trackers for {AnimeId}", animeId);
        }
    }



    /// <summary>
    /// Awaits every fire-and-forget AddEntry that hasn't yet committed to the DB.
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
