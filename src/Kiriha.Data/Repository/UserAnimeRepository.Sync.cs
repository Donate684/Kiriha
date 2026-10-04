using Kiriha.Core.Domain.Collections;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public sealed partial class UserAnimeRepository
{
    public async Task SyncFromRemoteAsync(IEnumerable<AnimeEntity> items, MediaKind[]? syncKinds = null, CancellationToken ct = default)
    {
        var incomingItems = items.DistinctBy(x => x.Id).ToList(); // materialize and deduplicate to avoid multiple evaluations and UNIQUE violations

        using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Safety check: if the API returned an empty list while we have meaningful
        // local state, treat it as a transient failure and refuse to wipe.
        if (incomingItems.Count == 0)
        {
            var query = context.UserAnime.AsQueryable();
            if (syncKinds != null && syncKinds.Length > 0)
            {
                query = query.Where(x => syncKinds.Contains(x.MediaKind));
            }

            var localCount = await query.CountAsync(ct);
            if (localCount > 10)
            {
                Log.Warning("Sync: Incoming list is empty but local DB has {Count} items. Skipping full deletion for safety.", localCount);
                return;
            }
        }

        // The context runs with NoTracking globally — opt into a transaction so
        // the upsert/delete happens atomically.
        using var transaction = await context.Database.BeginTransactionAsync(ct);

        try
        {
            var query = context.UserAnime.AsTracking().AsQueryable();
            if (syncKinds != null && syncKinds.Length > 0)
            {
                query = query.Where(x => syncKinds.Contains(x.MediaKind));
            }

            var existingItems = await query.ToListAsync(ct);
            var reconciliation = CollectionReconciliation.Reconcile(
                existingItems,
                incomingItems,
                x => x.Id,
                x => x.Id);

            if (reconciliation.LocalOnly.Count > 0)
            {
                context.UserAnime.RemoveRange(reconciliation.LocalOnly);
                var sample = string.Join(", ", reconciliation.LocalOnly.Take(10).Select(x => $"{x.Id}:{x.Title}"));
                Log.Information("Sync: Removing {Count} items from DB. Sample: {Sample}", reconciliation.LocalOnly.Count, sample);
            }

            foreach (var (existing, item) in reconciliation.Matched)
            {
                context.Entry(existing).CurrentValues.SetValues(item);
            }

            if (reconciliation.RemoteOnly.Count > 0)
            {
                var distinctRemote = reconciliation.RemoteOnly.DistinctBy(x => x.Id).ToList();
                var remoteIds = distinctRemote.Select(x => x.Id).ToList();
                var existingAnyKind = await context.UserAnime
                    .AsTracking()
                    .Where(x => remoteIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct);

                var toAdd = new List<AnimeEntity>();
                foreach (var item in distinctRemote)
                {
                    if (existingAnyKind.TryGetValue(item.Id, out var existingRow))
                    {
                        context.Entry(existingRow).CurrentValues.SetValues(item);
                    }
                    else
                    {
                        toAdd.Add(item);
                    }
                }

                if (toAdd.Count > 0)
                {
                    context.UserAnime.AddRange(toAdd);
                }
            }

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            Log.Information("Sync: Database update completed. Total items in incoming list: {Count}", incomingItems.Count);
        }
        catch (System.Exception ex)
        {
            await transaction.RollbackAsync(ct);
            Log.Error(ex, "Failed to sync anime list to EF Core database");
        }
    }
}
