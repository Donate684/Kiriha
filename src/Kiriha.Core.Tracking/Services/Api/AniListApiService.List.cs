using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class AniListApiService
{
    public async Task<List<AnimeEntity>?> GetUserAnimeListAsync(CancellationToken ct = default)
    {
        Log.Information("Syncing user list from AniList...");
        var account = _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList);
        var tokens = account?.Tokens as AniListTokens;
        if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
        {
            Log.Warning("AniList: not authenticated, skipping list sync");
            return null;
        }

        const string query = """
        query ($userId: Int, $userName: String) {
          MediaListCollection(userId: $userId, userName: $userName, type: ANIME) {
            lists {
              name
              status
              entries {
                id
                status
                score(format: POINT_10_DECIMAL)
                progress
                repeat
                notes
                media {
                  id
                  idMal
                  title {
                    romaji
                    english
                    native
                  }
                  coverImage {
                    large
                    extraLarge
                  }
                  bannerImage
                  description(asHtml: false)
                  episodes
                  format
                  status
                  averageScore
                  popularity
                  startDate { year month day }
                  genres
                }
              }
            }
          }
        }
        """;

        try
        {
            var variables = new Dictionary<string, object>();
            if (tokens.UserId.HasValue && tokens.UserId.Value > 0)
            {
                variables["userId"] = tokens.UserId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(tokens.UserName))
            {
                variables["userName"] = tokens.UserName;
            }

            using var doc = await ExecuteGraphQlAsync(query, variables, ct);
            if (doc == null) return null;

            var list = new List<AnimeEntity>();
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("MediaListCollection", out var collection) &&
                collection.TryGetProperty("lists", out var lists) &&
                lists.ValueKind == JsonValueKind.Array)
            {
                foreach (var listObj in lists.EnumerateArray())
                {
                    if (listObj.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                            if (entry.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
                            {
                                var anime = AniListMapper.MapMediaToAnimeEntity(media);
                                AniListMapper.MapEntryToUserStatus(entry, anime);

                                if (media.TryGetProperty("id", out var aniIdProp) && aniIdProp.TryGetInt32(out var aniId))
                                {
                                    _malToAniListMap[anime.Id] = aniId;
                                }

                                list.Add(anime);
                            }
                        }
                    }
                }
            }

            Log.Information("AniList: fetched {Count} entries from user library", list.Count);
            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "AniList: failed to fetch user anime list");
            return null;
        }
    }

    public async Task<SyncOutcome> UpdateProgressAsync(
        int animeId,
        int episodes,
        UserAnimeStatus? status = null,
        int? score = null,
        bool? isRewatching = null,
        int? rewatchCount = null,
        CancellationToken ct = default)
    {
        var account = _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList);
        if (account?.Tokens == null) return SyncOutcome.PermanentFailure;

        var mediaId = await ResolveAniListMediaIdAsync(animeId, "ANIME", ct);
        if (mediaId == null || mediaId <= 0)
        {
            Log.Warning("AniList: could not resolve mediaId for anime {AnimeId}", animeId);
            return SyncOutcome.TransientFailure;
        }

        const string mutation = """
        mutation ($mediaId: Int, $progress: Int, $status: MediaListStatus, $score: Float, $repeat: Int) {
          SaveMediaListEntry (mediaId: $mediaId, progress: $progress, status: $status, score: $score, repeat: $repeat) {
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
        if (score.HasValue && score.Value > 0) variables["score"] = (double)score.Value;
        if (rewatchCount.HasValue) variables["repeat"] = rewatchCount.Value;

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
        int? score = int.TryParse(item.Score, out var s) && s > 0 ? s : null;
        return UpdateProgressAsync(
            item.Id,
            item.Progress,
            item.Status,
            score,
            item.IsRewatching,
            item.RewatchCount,
            ct);
    }

    public async Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default)
    {
        var mediaId = await ResolveAniListMediaIdAsync(animeId, "ANIME", ct);
        if (mediaId == null || mediaId <= 0) return SyncOutcome.PermanentFailure;

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
            if (queryDoc != null &&
                queryDoc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Media", out var media) &&
                media.TryGetProperty("mediaListEntry", out var mle) &&
                mle.ValueKind == JsonValueKind.Object &&
                mle.TryGetProperty("id", out var entryIdProp) &&
                entryIdProp.TryGetInt32(out var entryId))
            {
                using var delDoc = await ExecuteGraphQlAsync(deleteMutation, new { id = entryId }, ct);
                if (delDoc != null)
                {
                    Log.Information("AniList: removed entry {EntryId} for anime {AnimeId}", entryId, animeId);
                    return SyncOutcome.Success;
                }
            }

            return SyncOutcome.Success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AniList: error removing anime {AnimeId}", animeId);
            return SyncOutcome.TransientFailure;
        }
    }

    public async Task<List<AnimeEntity>?> GetUserMangaListAsync(CancellationToken ct = default)
    {
        var account = _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList);
        var tokens = account?.Tokens as AniListTokens;
        if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken)) return null;

        const string query = """
        query ($userId: Int, $userName: String) {
          MediaListCollection(userId: $userId, userName: $userName, type: MANGA) {
            lists {
              name
              status
              entries {
                id
                status
                score(format: POINT_10_DECIMAL)
                progress
                progressVolumes
                repeat
                notes
                media {
                  id
                  idMal
                  title {
                    romaji
                    english
                    native
                  }
                  coverImage {
                    large
                  }
                  description(asHtml: false)
                  chapters
                  volumes
                  format
                  status
                  averageScore
                  genres
                }
              }
            }
          }
        }
        """;

        try
        {
            var variables = new Dictionary<string, object>();
            if (tokens.UserId.HasValue && tokens.UserId.Value > 0)
            {
                variables["userId"] = tokens.UserId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(tokens.UserName))
            {
                variables["userName"] = tokens.UserName;
            }

            using var doc = await ExecuteGraphQlAsync(query, variables, ct);
            if (doc == null) return null;

            var list = new List<AnimeEntity>();
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("MediaListCollection", out var collection) &&
                collection.TryGetProperty("lists", out var lists) &&
                lists.ValueKind == JsonValueKind.Array)
            {
                foreach (var listObj in lists.EnumerateArray())
                {
                    if (listObj.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                            if (entry.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
                            {
                                var manga = AniListMapper.MapMediaToAnimeEntity(media);
                                AniListMapper.MapEntryToUserStatus(entry, manga);

                                if (media.TryGetProperty("id", out var aniIdProp) && aniIdProp.TryGetInt32(out var aniId))
                                {
                                    _malToAniListMap[manga.Id] = aniId;
                                }

                                list.Add(manga);
                            }
                        }
                    }
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AniList: failed to fetch user manga list");
            return null;
        }
    }

    public async Task<SyncOutcome> UpdateMangaProgressAsync(
        int mangaId,
        int chapters,
        int? volumes = null,
        UserAnimeStatus? status = null,
        int? score = null,
        CancellationToken ct = default)
    {
        var mediaId = await ResolveAniListMediaIdAsync(mangaId, "MANGA", ct);
        if (mediaId == null || mediaId <= 0) return SyncOutcome.PermanentFailure;

        const string mutation = """
        mutation ($mediaId: Int, $progress: Int, $progressVolumes: Int, $status: MediaListStatus, $score: Float) {
          SaveMediaListEntry (mediaId: $mediaId, progress: $progress, progressVolumes: $progressVolumes, status: $status, score: $score) {
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
        if (score.HasValue && score.Value > 0) variables["score"] = (double)score.Value;

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
