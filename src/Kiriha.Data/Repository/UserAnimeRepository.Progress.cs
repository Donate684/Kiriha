using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Repository;

public sealed partial class UserAnimeRepository
{
    public async Task UpdateProgressAsync(AnimeEntity item, int progress, UserAnimeStatus? status = null, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var affected = await UpdateProgressInContextAsync(context, item, progress, status, ct);
        if (affected == 0)
        {
            Log.Warning("Attempted to update progress for non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            await UpsertAsync(item, ct);
        }
    }

    internal static async Task<int> UpdateProgressInContextAsync(
        AppDbContext context, AnimeEntity item, int progress, UserAnimeStatus? status, CancellationToken ct)
    {
        var newStatus = status.GetValueOrDefault();
        var shouldUpdateStatus = newStatus != UserAnimeStatus.None;
        var isManga = item.MediaKind != MediaKind.Anime;

        int affected;

        if (isManga)
        {
            affected = shouldUpdateStatus
                ? await context.UserAnime
                    .Where(x => x.Id == item.Id && x.MediaKind == item.MediaKind)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.ChaptersRead, item.ChaptersRead)
                        .SetProperty(x => x.VolumesRead, item.VolumesRead)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted)
                        .SetProperty(x => x.Status, newStatus), ct)
                : await context.UserAnime
                    .Where(x => x.Id == item.Id && x.MediaKind == item.MediaKind)
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
                    .Where(x => x.Id == item.Id && x.MediaKind == item.MediaKind)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted)
                        .SetProperty(x => x.Status, newStatus), ct)
                : await context.UserAnime
                    .Where(x => x.Id == item.Id && x.MediaKind == item.MediaKind)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted), ct);
        }

        return affected;
    }

    public async Task UpdateScoreAsync(AnimeEntity item, string score, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);

        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id && x.MediaKind == item.MediaKind)
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
        var genres = new List<string>(item.Genres);
        var studios = new List<string>(item.Studios);
        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id && x.MediaKind == item.MediaKind)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Title, item.Title)
                .SetProperty(x => x.RussianTitle, item.RussianTitle)
                .SetProperty(x => x.RussianSynopsis, item.RussianSynopsis)
                .SetProperty(x => x.EnglishTitle, item.EnglishTitle)
                .SetProperty(x => x.JapaneseTitle, item.JapaneseTitle)
                .SetProperty(x => x.MainPictureUrl, item.MainPictureUrl)
                .SetProperty(x => x.LocalPosterPath, item.LocalPosterPath)
                .SetProperty(x => x.TotalEpisodes, item.TotalEpisodes)
                .SetProperty(x => x.Genres, genres)
                .SetProperty(x => x.Studios, studios)
                .SetProperty(x => x.AlternativeTitles, alternativeTitles), ct);

        if (affected == 0)
        {
            Log.Debug("Skipping metadata-only update for non-user anime {Title} (ID: {Id})", item.Title, item.Id);
        }
    }
}
