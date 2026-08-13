using Kiriha.Core.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public sealed partial class UserAnimeRepository
{
    public async Task SyncFromRemoteAsync(IEnumerable<AnimeEntity> items, MediaKind[]? syncKinds = null, CancellationToken ct = default)
    {
        var incomingItems = items.ToList(); // materialize to avoid multiple evaluations
        var incomingIds = incomingItems.Select(x => x.Id).ToHashSet();

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

        // The context runs with NoTracking globally â€” opt into a transaction so
        // the upsert/delete happens atomically.
        using var transaction = await context.Database.BeginTransactionAsync(ct);

        try
        {
            var query = context.UserAnime.AsQueryable();
            if (syncKinds != null && syncKinds.Length > 0)
            {
                query = query.Where(x => syncKinds.Contains(x.MediaKind));
            }

            var existingItems = await query.ToListAsync(ct);
            var toRemove = existingItems.Where(x => !incomingIds.Contains(x.Id)).ToList();
            if (toRemove.Count > 0)
            {
                context.UserAnime.RemoveRange(toRemove);
                var sample = string.Join(", ", toRemove.Take(10).Select(x => $"{x.Id}:{x.Title}"));
                Log.Information("Sync: Removing {Count} items from DB. Sample: {Sample}", toRemove.Count, sample);
            }

            var existingItemsDict = existingItems.ToDictionary(x => x.Id);

            foreach (var item in incomingItems)
            {
                if (existingItemsDict.TryGetValue(item.Id, out var existing))
                {
                    context.Entry(existing).CurrentValues.SetValues(item);
                }
                else
                {
                    context.UserAnime.Add(item);
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
