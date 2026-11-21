using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Repository;

public sealed partial class SyncTaskRepository
{
    public async Task<HistoryItem?> GetHistoryEntryAsync(int historyId, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.History.SingleOrDefaultAsync(h => h.Id == historyId, ct);
    }

    public async Task<HistoryItem?> CheckpointDeliveryAsync(
        SyncTaskEntity task, string trackerName, TrackerSyncState state, string? error, CancellationToken ct)
    {
        var payload = OwnedSyncTaskPayload.Read(task.Payload);
        if (payload?.HistoryId is not { } historyId)
            throw new InvalidOperationException("Cannot checkpoint delivery without an original history entry.");

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var item = await context.History.AsTracking().SingleOrDefaultAsync(h => h.Id == historyId, ct);
        if (item != null && FindOwner(item, trackerName) == payload.Owner)
            item.SetTrackerStatus(trackerName, state, error);
        else
            item = null;

        // A purged history row does not prevent the tracker queue from completing.
        context.SyncTasks.Update(task);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return item;
    }

    public async Task<HistoryItem?> QueueHistoryRetryAsync(int historyId, string trackerName, SyncTaskEntity task, CancellationToken ct)
    {
        var payload = OwnedSyncTaskPayload.Read(task.Payload);
        if (payload?.HistoryId != historyId || payload is null)
            return null;

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var item = await context.History.AsTracking().SingleOrDefaultAsync(h => h.Id == historyId, ct);
        if (item == null || item.AnimeId != task.AnimeId || item.MediaKind != task.MediaKind ||
            !item.TrackerStatuses.TryGetValue(trackerName, out var status) || status.State != TrackerSyncState.Failed ||
            FindOwner(item, trackerName) != payload.Owner)
            return null;

        var laterEntries = await context.History
            .Where(h => h.Id > historyId && h.AnimeId == task.AnimeId && h.MediaKind == task.MediaKind && h.TrackerStatusJson != null)
            .ToListAsync(ct);
        if (laterEntries.Any(entry => entry.TrackerStatuses.Keys.Any(name => FindOwner(entry, name) == payload.Owner)))
            return null;

        var pending = await context.SyncTasks.Where(q => q.AnimeId == task.AnimeId && q.MediaKind == task.MediaKind)
            .Select(q => q.Payload).ToListAsync(ct);
        if (pending.Any(json => OwnedSyncTaskPayload.Read(json)?.Owner == payload.Owner))
            return null;

        item.SetTrackerStatus(trackerName, TrackerSyncState.Pending);
        context.SyncTasks.Add(task);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return item;
    }

    private static SyncTaskOwner? FindOwner(HistoryItem entry, string trackerName) =>
        entry.TrackerStatuses.TryGetValue(trackerName, out var status)
            ? OwnedSyncTaskPayload.Read(status.OriginalTask?.Payload)?.Owner : null;
}
