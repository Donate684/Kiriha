using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync.Models;

namespace Kiriha.Core.Tracking.Sync;

public partial class SyncManager
{
    private List<(SyncTask Task, string? Name)> BindDestinations(SyncTask mutation)
    {
        if (_settingsService is null)
            return [(mutation, null)];

        var api = _settingsService.Current.Api;
        var primary = api.GetPrimaryAccount();
        var destinations = new List<(SyncTask, string?)>();
        foreach (var tracker in _trackers)
        {
            var account = api.GetAccount(tracker.TrackerId);
            if (account is not { IsEnabled: true, Tokens.AccessToken.Length: > 0 })
                continue;

            if (mutation.TargetTracker is { Length: > 0 } target)
            {
                if (!string.Equals(target, tracker.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(target, tracker.TrackerId, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            else if (!ReferenceEquals(account, primary) && !account.IsMirror)
            {
                continue;
            }

            destinations.Add((new SyncTask
            {
                AnimeId = mutation.AnimeId,
                MediaKind = mutation.MediaKind,
                Type = mutation.Type,
                Progress = mutation.Progress,
                Status = mutation.Status,
                Score = mutation.Score,
                FullItem = mutation.FullItem?.Clone(),
                Owner = SyncTaskOwner.FromAccount(account),
                IsDirect = !string.IsNullOrEmpty(mutation.TargetTracker)
            }, tracker.Name));
        }

        return destinations;
    }

    private async Task EnqueueOwnedMutationAsync(SyncTask mutation)
    {
        var destinations = BindDestinations(mutation);
        if (destinations.Count == 0)
            return;

        var ct = _cts.Token;
        await _dbInit.InitializationTask.WaitAsync(ct);
        var entities = destinations.Select(x => MapToEntity(x.Task)).ToArray();
        await _queuePersistenceGate.WaitAsync(ct);
        try
        {
            await _syncTaskRepo.AddRangeAsync(entities, ct);
            QueueCommittedDestinations(destinations, entities);
        }
        finally
        {
            _queuePersistenceGate.Release();
        }
    }

    private TrackerAccount? GetActiveOwner(SyncTaskOwner owner, bool isDirect)
    {
        var api = _settingsService?.Current.Api;
        var account = api?.GetAccount(owner.TrackerId);
        return account is { IsEnabled: true, Tokens.AccessToken.Length: > 0 } &&
            (isDirect || account.IsMirror || ReferenceEquals(account, api?.GetPrimaryAccount())) &&
            SyncTaskOwner.FromAccount(account) == owner ? account : null;
    }

    private ITrackerService? GetOwnerTracker(SyncTaskOwner owner) =>
        _trackers.FirstOrDefault(t => string.Equals(t.TrackerId, owner.TrackerId, StringComparison.OrdinalIgnoreCase));

    internal async Task<bool> CommitEntryChangeAsync(
        int id, MediaKind kind, AnimeEntity? item, IReadOnlyList<HistoryItem> history, CancellationToken ct)
    {
        var repository = _progressChangeRepository ?? throw new InvalidOperationException("Library entry persistence is not configured.");
        var mutation = item is null
            ? new SyncTask { AnimeId = id, MediaKind = kind, Type = SyncTaskType.Remove }
            : CreateFullUpdateTask(item);
        var destinations = BindDestinations(mutation);
        if (history.LastOrDefault() is { } finalEntry)
            AttachOriginalDeliveries(finalEntry, destinations);

        var entities = destinations.Select(x => MapToEntity(x.Task)).ToArray();
        await _dbInit.InitializationTask.WaitAsync(ct);
        await _queuePersistenceGate.WaitAsync(ct);
        try
        {
            await repository.SaveEntryAsync(id, kind, item, history, entities, ct);
            foreach (var entry in history)
                _historyService.PublishCommittedEntry(entry);
            QueueCommittedDestinations(destinations, entities);
        }
        finally
        {
            _queuePersistenceGate.Release();
        }

        return destinations.Count > 0;
    }

    private void AttachOriginalDeliveries(HistoryItem history, List<(SyncTask Task, string? Name)> destinations)
    {
        foreach (var (task, name) in destinations)
        {
            if (name != null)
                history.SetTrackerStatus(name, TrackerSyncState.Pending, originalTask: MapToEntity(task));
        }
    }
}
