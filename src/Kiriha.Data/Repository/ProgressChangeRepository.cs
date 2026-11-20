using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Repository;

public sealed class ProgressChangeRepository(IDbContextFactory<AppDbContext> contextFactory) : IProgressChangeRepository
{
    public async Task<bool> SaveAsync(
        AnimeEntity item, UserAnimeStatus? nextStatus, HistoryItem history, SyncTaskEntity task, bool onlyAdvance, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // Read under the SQLite write transaction so concurrent scrobbles cannot both record the episode.
        bool isManga = item.MediaKind != MediaKind.Anime;
        var previousProgress = await context.UserAnime
            .Where(x => x.Id == item.Id && x.MediaKind == item.MediaKind)
            .Select(x => (int?)(isManga ? x.ChaptersRead : x.Progress))
            .SingleOrDefaultAsync(ct);
        if (onlyAdvance && previousProgress >= history.Episode)
            return false;

        if (previousProgress.HasValue)
        {
            await UserAnimeRepository.UpdateProgressInContextAsync(context, item, history.Episode, nextStatus, ct);
        }
        else
        {
            if (item.Id <= 0 || string.IsNullOrWhiteSpace(item.Title) || item.Status == UserAnimeStatus.None)
                throw new InvalidOperationException("Cannot save progress without a valid library entry.");

            context.UserAnime.Add(item);
        }

        context.History.Add(history);
        context.SyncTasks.Add(task);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
