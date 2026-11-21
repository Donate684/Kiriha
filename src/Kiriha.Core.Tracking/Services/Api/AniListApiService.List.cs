using Kiriha.Core.Tracking.Auth;
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
        var account = TrackerRequestAccount.Get(_settingsService, TrackerConstants.Ids.AniList);
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
                  synonyms
                  countryOfOrigin
                  studios {
                    nodes {
                      name
                      isAnimationStudio
                    }
                  }
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
            var seenMediaIds = new HashSet<int>();
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
                                    bool isNativeId = !(media.TryGetProperty("idMal", out var malProp) && malProp.ValueKind == JsonValueKind.Number && malProp.TryGetInt32(out var malVal) && malVal > 0);
                                    await CacheMediaMappingAsync(anime.Id, "ANIME", aniId, isNativeId);
                                    if (!seenMediaIds.Add(aniId)) continue;
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

    public async Task<List<AnimeEntity>?> GetUserMangaListAsync(CancellationToken ct = default)
    {
        var account = TrackerRequestAccount.Get(_settingsService, TrackerConstants.Ids.AniList);
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
                  synonyms
                  countryOfOrigin
                  studios {
                    nodes {
                      name
                      isAnimationStudio
                    }
                  }
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
            var seenMediaIds = new HashSet<int>();
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
                                    bool isNativeId = !(media.TryGetProperty("idMal", out var malProp) && malProp.ValueKind == JsonValueKind.Number && malProp.TryGetInt32(out var malVal) && malVal > 0);
                                    await CacheMediaMappingAsync(manga.Id, "MANGA", aniId, isNativeId);
                                    if (!seenMediaIds.Add(aniId)) continue;
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
}
