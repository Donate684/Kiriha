using Kiriha.Core.Abstractions.Repositories;
using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Repository;

public sealed class ProgressChangeRepository(IDbContextFactory<AppDbContext> contextFactory) : IProgressChangeRepository
{
    public async Task SaveEntryAsync(int id, MediaKind kind, AnimeEntity? item, IReadOnlyList<HistoryItem> history,
        IReadOnlyList<SyncTaskEntity> tasks, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (item is null)
        {
            await context.UserAnime.Where(row => row.Id == id && row.MediaKind == kind).ExecuteDeleteAsync(ct);
        }
        else
        {
            if (item.Id != id || item.MediaKind != kind || item.Status == UserAnimeStatus.None ||
                id <= 0 || string.IsNullOrWhiteSpace(item.Title))
                throw new InvalidOperationException("Cannot save an invalid library entry.");

            await UserAnimeRepository.UpsertInContextAsync(context, item, ct);
        }

        context.History.AddRange(history);
        await context.SaveChangesAsync(ct);
        await SaveQueueAsync(context, tasks, history.LastOrDefault(), ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<bool> SaveAsync(
        AnimeEntity item, UserAnimeStatus? nextStatus, HistoryItem history, IReadOnlyList<SyncTaskEntity> tasks,
        bool onlyAdvance, CancellationToken ct)
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
        await context.SaveChangesAsync(ct);
        await SaveQueueAsync(context, tasks, history, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static async Task SaveQueueAsync(
        AppDbContext context, IReadOnlyList<SyncTaskEntity> tasks, HistoryItem? history, CancellationToken ct)
    {
        if (tasks.Count == 0)
            return;

        foreach (var task in tasks)
        {
            if (history != null && OwnedSyncTaskPayload.Read(task.Payload) is { } payload)
                task.Payload = JsonSerializer.Serialize(payload with { HistoryId = history.Id });
        }
        context.SyncTasks.AddRange(tasks);
        await context.SaveChangesAsync(ct);
    }
}
