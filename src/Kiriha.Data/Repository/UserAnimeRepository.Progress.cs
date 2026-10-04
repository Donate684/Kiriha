using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public sealed partial class UserAnimeRepository
{
    public async Task UpdateProgressAsync(AnimeEntity item, int progress, UserAnimeStatus? status = null, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);

        var shouldUpdateStatus = status.HasValue && status.Value != UserAnimeStatus.None;
        var isManga = item.MediaKind != MediaKind.Anime;

        int affected;

        if (isManga)
        {
            affected = shouldUpdateStatus
                ? await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.ChaptersRead, item.ChaptersRead)
                        .SetProperty(x => x.VolumesRead, item.VolumesRead)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted)
                        .SetProperty(x => x.Status, status!.Value), ct)
                : await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.ChaptersRead, item.ChaptersRead)
                        .SetProperty(x => x.VolumesRead, item.VolumesRead)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted), ct);
        }
        else
        {
            affected = shouldUpdateStatus
                ? await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted)
                        .SetProperty(x => x.Status, status!.Value), ct)
                : await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted), ct);
        }

        if (affected == 0)
        {
            Log.Warning("Attempted to update progress for non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            await UpsertAsync(item, ct);
            return;
        }
    }

    public async Task UpdateScoreAsync(AnimeEntity item, string score, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);

        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Score, score), ct);

        if (affected == 0)
        {
            Log.Warning("Attempted to update score for non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            await UpsertAsync(item, ct);
        }
    }

    public async Task UpdateMetadataAsync(AnimeEntity item, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);

        var alternativeTitles = new List<string>(item.AlternativeTitles);
        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Title, item.Title)
                .SetProperty(x => x.RussianTitle, item.RussianTitle)
                .SetProperty(x => x.RussianSynopsis, item.RussianSynopsis)
                .SetProperty(x => x.EnglishTitle, item.EnglishTitle)
                .SetProperty(x => x.JapaneseTitle, item.JapaneseTitle)
                .SetProperty(x => x.MainPictureUrl, item.MainPictureUrl)
                .SetProperty(x => x.LocalPosterPath, item.LocalPosterPath)
                .SetProperty(x => x.AlternativeTitles, alternativeTitles), ct);

        if (affected == 0)
        {
            Log.Debug("Skipping metadata-only update for non-user anime {Title} (ID: {Id})", item.Title, item.Id);
        }
    }
}
