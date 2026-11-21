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

    public async Task<bool> MergeDetailsMetadataAsync(int id, MediaKind kind, AnimeEntity details, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.UserAnime.AsTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.MediaKind == kind, ct);
        if (existing == null) return false;

        // The JSON converters track list references. The merge edits their contents in place.
        var entry = context.Entry(existing);
        bool genresChanged = details.Genres.Count > 0 && !existing.Genres.SequenceEqual(details.Genres);
        bool studiosChanged = details.Studios.Count > 0 && !existing.Studios.SequenceEqual(details.Studios);
        bool alternativeTitlesChanged = details.AlternativeTitles.Any(title => !existing.AlternativeTitles.Contains(title));
        existing.MergeFullDetails(details, metadataOnly: true);
        entry.Property(item => item.Genres).IsModified = genresChanged;
        entry.Property(item => item.Studios).IsModified = studiosChanged;
        entry.Property(item => item.AlternativeTitles).IsModified = alternativeTitlesChanged;
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateAiringAsync(AnimeEntity item, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id && x.MediaKind == item.MediaKind)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.EpisodesAired, item.EpisodesAired)
                .SetProperty(x => x.NextEpisodeAt, item.NextEpisodeAt)
                .SetProperty(x => x.LastEpisodeAt, item.LastEpisodeAt)
                .SetProperty(x => x.LastEpisodesSync, item.LastEpisodesSync)
                .SetProperty(x => x.AiredSourcePriority, item.AiredSourcePriority)
                .SetProperty(x => x.StatusDetailed, item.StatusDetailed)
                .SetProperty(x => x.TotalEpisodes, item.TotalEpisodes), ct);
        return affected > 0;
    }
}
