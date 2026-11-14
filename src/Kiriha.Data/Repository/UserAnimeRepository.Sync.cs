using Kiriha.Core.Domain.Collections;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public sealed partial class UserAnimeRepository
{
    public async Task SyncFromRemoteAsync(IEnumerable<AnimeEntity> items, MediaKind[]? syncKinds = null, CancellationToken ct = default)
    {
        var incomingItems = items.DistinctBy(x => (x.Id, x.MediaKind)).ToList(); // materialize and deduplicate to avoid multiple evaluations and UNIQUE violations

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
                x => (x.Id, x.MediaKind),
                x => (x.Id, x.MediaKind));

            if (reconciliation.LocalOnly.Count > 0)
            {
                context.UserAnime.RemoveRange(reconciliation.LocalOnly);
                var sample = string.Join(", ", reconciliation.LocalOnly.Take(10).Select(x => $"{x.Id}:{x.Title}"));
                Log.Information("Sync: Removing {Count} items from DB. Sample: {Sample}", reconciliation.LocalOnly.Count, sample);
            }

            foreach (var (existing, item) in reconciliation.Matched)
            {
                var localPoster = existing.LocalPosterPath;
                var russianTitle = existing.RussianTitle;
                var russianSynopsis = existing.RussianSynopsis;
                var nextEpisodeAt = existing.NextEpisodeAt;
                var lastEpisodesSync = existing.LastEpisodesSync;
                var totalEpisodes = existing.TotalEpisodes;

                context.Entry(existing).CurrentValues.SetValues(item);

                if (string.IsNullOrEmpty(item.LocalPosterPath) && !string.IsNullOrEmpty(localPoster))
                    existing.LocalPosterPath = localPoster;
                if (string.IsNullOrEmpty(item.RussianTitle) && !string.IsNullOrEmpty(russianTitle))
                    existing.RussianTitle = russianTitle;
                if (string.IsNullOrEmpty(item.RussianSynopsis) && !string.IsNullOrEmpty(russianSynopsis))
                    existing.RussianSynopsis = russianSynopsis;
                if (!item.NextEpisodeAt.HasValue && nextEpisodeAt.HasValue)
                    existing.NextEpisodeAt = nextEpisodeAt;
                if (!item.LastEpisodesSync.HasValue && lastEpisodesSync.HasValue)
                    existing.LastEpisodesSync = lastEpisodesSync;
                if (item.TotalEpisodes <= 0 && totalEpisodes > 0)
                    existing.TotalEpisodes = totalEpisodes;
            }

            if (reconciliation.RemoteOnly.Count > 0)
            {
                var distinctRemote = reconciliation.RemoteOnly.DistinctBy(x => (x.Id, x.MediaKind)).ToList();
                context.UserAnime.AddRange(distinctRemote);
            }

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            Log.Information("Sync: Database update completed. Total items in incoming list: {Count}", incomingItems.Count);
        }
        catch (System.Exception ex)
        {
            await transaction.RollbackAsync(ct);
            Log.Error(ex, "Failed to sync anime list to EF Core database");
            throw;
        }
    }
}
