using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync.Models;

namespace Kiriha.Core.Tracking.Sync;

public partial class SyncManager
{
    public async Task<HistoryRetryResult> RetryHistoryDeliveryAsync(int historyId, string trackerName, CancellationToken ct = default)
    {
        if (historyId <= 0 || _settingsService is null)
            return HistoryRetryResult.NotRetryable;

        await _dbInit.InitializationTask.WaitAsync(ct);
        await _queuePersistenceGate.WaitAsync(ct);
        try
        {
            var entry = await _syncTaskRepo.GetHistoryEntryAsync(historyId, ct);
            if (entry == null || !entry.TrackerStatuses.TryGetValue(trackerName, out var status) ||
                status is not { State: TrackerSyncState.Failed, OriginalTask: { } original } ||
                original.AnimeId != entry.AnimeId || original.MediaKind != entry.MediaKind ||
                OwnedSyncTaskPayload.Read(original.Payload) is not { } payload ||
                !Enum.TryParse<SyncTaskType>(original.Type, out var type))
                return HistoryRetryResult.NotRetryable;

            if (GetActiveOwner(payload.Owner, isDirect: true) is null || GetOwnerTracker(payload.Owner) is null)
                return HistoryRetryResult.AccountUnavailable;

            var retry = new SyncTask
            {
                AnimeId = original.AnimeId,
                MediaKind = original.MediaKind,
                Type = type,
                Progress = original.Progress,
                Score = original.Score,
                Status = original.Status is { } value ? StatusMapper.FromDbString(value) : null,
                FullItem = payload.Item,
                Owner = payload.Owner,
                IsDirect = true,
                HistoryId = historyId
            };
            var entity = MapToEntity(retry);
            var updated = await _syncTaskRepo.QueueHistoryRetryAsync(historyId, trackerName, entity, ct);
            if (updated is null)
                return HistoryRetryResult.NotRetryable;

            _historyService.PublishUpdatedEntry(updated);
            QueueCommittedDestinations([(retry, trackerName)], [entity]);
            return HistoryRetryResult.Queued;
        }
        finally
        {
            _queuePersistenceGate.Release();
        }
    }

    private async Task CheckpointDeliveryAsync(SyncTaskEntity entity, string trackerName,
        TrackerSyncState state, string? error, CancellationToken ct)
    {
        var updated = await _syncTaskRepo.CheckpointDeliveryAsync(entity, trackerName, state, error, ct);
        if (updated != null)
            _historyService.PublishUpdatedEntry(updated);
    }

    private async Task RecordDiscardedDeliveryAsync(SyncTaskEntity entity, CancellationToken ct)
    {
        if (OwnedSyncTaskPayload.Read(entity.Payload) is not { HistoryId: not null } payload ||
            GetOwnerTracker(payload.Owner) is not { } tracker)
            return;

        var resolved = entity.SuccessfulTrackersJson is { } json
            ? JsonSerializer.Deserialize<HashSet<string>>(json) ?? [] : [];
        if (!resolved.Add(tracker.Name))
            return;

        entity.SuccessfulTrackersJson = JsonSerializer.Serialize(resolved);
        await CheckpointDeliveryAsync(entity, tracker.Name, TrackerSyncState.Superseded, null, ct);
    }
}
