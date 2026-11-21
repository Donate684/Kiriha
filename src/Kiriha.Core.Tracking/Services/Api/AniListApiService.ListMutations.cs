using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Auth;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class AniListApiService
{
    public Task<SyncOutcome> UpdateProgressAsync(
        int animeId,
        int episodes,
        UserAnimeStatus? status = null,
        int? score = null,
        bool? isRewatching = null,
        int? rewatchCount = null,
        CancellationToken ct = default) =>
        SaveAnimeEntryAsync(animeId, episodes, status, score, isRewatching, rewatchCount, notes: null, ct);

    private async Task<SyncOutcome> SaveAnimeEntryAsync(
        int animeId, int episodes, UserAnimeStatus? status, int? score,
        bool? isRewatching, int? rewatchCount, string? notes, CancellationToken ct)
    {
        var account = TrackerRequestAccount.Get(_settingsService, TrackerConstants.Ids.AniList);
        if (account?.Tokens == null) return SyncOutcome.PermanentFailure;

        var (mediaId, resolveOutcome) = await ResolveAniListMediaIdAsync(animeId, "ANIME", ct);
        if (mediaId == null || mediaId <= 0)
        {
            Log.Warning("AniList: could not resolve mediaId for anime {AnimeId} ({Outcome})", animeId, resolveOutcome);
            return resolveOutcome;
        }

        const string mutation = """
        mutation ($mediaId: Int, $progress: Int, $status: MediaListStatus, $score: Float, $repeat: Int, $notes: String) {
          SaveMediaListEntry (mediaId: $mediaId, progress: $progress, status: $status, score: $score, repeat: $repeat, notes: $notes) {
            id
            status
            progress
            score
          }
        }
        """;

        var aniStatus = StatusMapper.ToAniList(status, isRewatching ?? false);
        var variables = new Dictionary<string, object?>
        {
            ["mediaId"] = mediaId.Value,
            ["progress"] = episodes
        };

        if (!string.IsNullOrEmpty(aniStatus)) variables["status"] = aniStatus;
        if (score.HasValue) variables["score"] = (double)score.Value;
        if (rewatchCount.HasValue) variables["repeat"] = rewatchCount.Value;
        if (notes != null) variables["notes"] = notes;

        try
        {
            using var doc = await ExecuteGraphQlAsync(mutation, variables, ct);
            if (doc == null) return SyncOutcome.TransientFailure;

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("SaveMediaListEntry", out var entry) &&
                entry.ValueKind == JsonValueKind.Object)
            {
                Log.Information("AniList: updated progress for {AnimeId} (media {MediaId}) to ep {Ep}", animeId, mediaId, episodes);
                return SyncOutcome.Success;
            }

            return SyncOutcome.TransientFailure;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AniList: error updating progress for {AnimeId}", animeId);
            return SyncOutcome.TransientFailure;
        }
    }

    public Task<SyncOutcome> SaveFullListStatusAsync(AnimeEntity item, CancellationToken ct = default)
    {
        int? score = int.TryParse(item.Score, out var s) && s > 0 ? s : 0;
        if (item.MediaKind != MediaKind.Anime)
        {
            return SaveMangaEntryAsync(
                item.Id,
                item.ChaptersRead,
                item.VolumesRead,
                item.Status,
                score,
                item.Notes,
                ct);
        }

        return SaveAnimeEntryAsync(
            item.Id,
            item.Progress,
            item.Status,
            score,
            item.IsRewatching,
            item.RewatchCount,
            item.Notes,
            ct);
    }

    public Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default) =>
        RemoveMediaEntryAsync(animeId, "ANIME", ct);

    public Task<SyncOutcome> RemoveMangaAsync(int mangaId, CancellationToken ct = default) =>
        RemoveMediaEntryAsync(mangaId, "MANGA", ct);

    private async Task<SyncOutcome> RemoveMediaEntryAsync(int primaryId, string mediaType, CancellationToken ct)
    {
        var (mediaId, resolveOutcome) = await ResolveAniListMediaIdAsync(primaryId, mediaType, ct);
        if (mediaId == null || mediaId <= 0)
        {
            return resolveOutcome == SyncOutcome.PermanentFailure ? SyncOutcome.Success : resolveOutcome;
        }

        const string queryEntry = """
        query ($mediaId: Int) {
          Media(id: $mediaId) {
            mediaListEntry {
              id
            }
          }
        }
        """;

        const string deleteMutation = """
        mutation ($id: Int) {
          DeleteMediaListEntry (id: $id) {
            deleted
          }
        }
        """;

        try
        {
            using var queryDoc = await ExecuteGraphQlAsync(queryEntry, new { mediaId = mediaId.Value }, ct);
            if (queryDoc == null) return SyncOutcome.TransientFailure;

            if (queryDoc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Media", out var media))
            {
                if (media.TryGetProperty("mediaListEntry", out var mle) && mle.ValueKind == JsonValueKind.Object &&
                    mle.TryGetProperty("id", out var entryIdProp) && entryIdProp.TryGetInt32(out var entryId))
                {
                    using var delDoc = await ExecuteGraphQlAsync(deleteMutation, new { id = entryId }, ct);
                    if (delDoc != null &&
                        delDoc.RootElement.TryGetProperty("data", out var delData) &&
                        delData.TryGetProperty("DeleteMediaListEntry", out var delEntry) &&
                        delEntry.TryGetProperty("deleted", out var deletedProp) &&
                        deletedProp.GetBoolean())
                    {
                        Log.Information("AniList: removed entry {EntryId} for {Type} {PrimaryId}", entryId, mediaType, primaryId);
                        return SyncOutcome.Success;
                    }
                    return SyncOutcome.TransientFailure;
                }
                else
                {
                    Log.Information("AniList: entry for media {MediaId} not found in library; treated as removed", mediaId.Value);
                    return SyncOutcome.Success;
                }
            }

            return SyncOutcome.TransientFailure;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AniList: error removing {Type} {PrimaryId}", mediaType, primaryId);
            return SyncOutcome.TransientFailure;
        }
    }

    public Task<SyncOutcome> UpdateMangaProgressAsync(
        int mangaId,
        int chapters,
        int? volumes = null,
        UserAnimeStatus? status = null,
        int? score = null,
        CancellationToken ct = default) =>
        SaveMangaEntryAsync(mangaId, chapters, volumes, status, score, notes: null, ct);

    private async Task<SyncOutcome> SaveMangaEntryAsync(
        int mangaId, int chapters, int? volumes, UserAnimeStatus? status,
        int? score, string? notes, CancellationToken ct)
    {
        var (mediaId, resolveOutcome) = await ResolveAniListMediaIdAsync(mangaId, "MANGA", ct);
        if (mediaId == null || mediaId <= 0)
        {
            Log.Warning("AniList: could not resolve mediaId for manga {MangaId} ({Outcome})", mangaId, resolveOutcome);
            return resolveOutcome;
        }

        const string mutation = """
        mutation ($mediaId: Int, $progress: Int, $progressVolumes: Int, $status: MediaListStatus, $score: Float, $notes: String) {
          SaveMediaListEntry (mediaId: $mediaId, progress: $progress, progressVolumes: $progressVolumes, status: $status, score: $score, notes: $notes) {
            id
            progress
            status
          }
        }
        """;

        var aniStatus = StatusMapper.ToAniList(status);
        var variables = new Dictionary<string, object?>
        {
            ["mediaId"] = mediaId.Value,
            ["progress"] = chapters
        };

        if (volumes.HasValue) variables["progressVolumes"] = volumes.Value;
        if (!string.IsNullOrEmpty(aniStatus)) variables["status"] = aniStatus;
        if (score.HasValue) variables["score"] = (double)score.Value;
        if (notes != null) variables["notes"] = notes;

        try
        {
            using var doc = await ExecuteGraphQlAsync(mutation, variables, ct);
            return doc != null ? SyncOutcome.Success : SyncOutcome.TransientFailure;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AniList: error updating manga progress for {MangaId}", mangaId);
            return SyncOutcome.TransientFailure;
        }
    }
}
