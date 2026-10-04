using System.Text.Json;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class AniListApiService
{
    public async Task<List<AnimeEntity>> SearchAnimeAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<AnimeEntity>();

        const string gql = """
        query ($search: String) {
          Page(page: 1, perPage: 25) {
            media(search: $search, type: ANIME) {
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
        """;

        try
        {
            using var doc = await ExecuteGraphQlAsync(gql, new { search = query }, ct);
            if (doc == null) return new List<AnimeEntity>();

            var list = new List<AnimeEntity>();
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Page", out var page) &&
                page.TryGetProperty("media", out var mediaArr) &&
                mediaArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var media in mediaArr.EnumerateArray())
                {
                    var item = AniListMapper.MapMediaToAnimeEntity(media);
                    if (media.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var aniId))
                    {
                        _malToAniListMap[item.Id] = aniId;
                    }
                    list.Add(item);
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: SearchAnime failed for query {Query}", query);
            return new List<AnimeEntity>();
        }
    }

    public async Task<AnimeEntity?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default)
    {
        const string queryByMal = """
        query ($malId: Int) {
          Media(idMal: $malId, type: ANIME) {
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
        """;

        try
        {
            using var doc = await ExecuteGraphQlAsync(queryByMal, new { malId = animeId }, ct);
            if (doc != null &&
                doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Media", out var media) &&
                media.ValueKind == JsonValueKind.Object)
            {
                var item = AniListMapper.MapMediaToAnimeEntity(media);
                if (media.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var aniId))
                {
                    _malToAniListMap[item.Id] = aniId;
                }
                return item;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: GetAnimeDetails failed for {AnimeId}", animeId);
        }

        return null;
    }

    public async Task<List<AnimeEntity>> SearchMangaAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<AnimeEntity>();

        const string gql = """
        query ($search: String) {
          Page(page: 1, perPage: 25) {
            media(search: $search, type: MANGA) {
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
        """;

        try
        {
            using var doc = await ExecuteGraphQlAsync(gql, new { search = query }, ct);
            if (doc == null) return new List<AnimeEntity>();

            var list = new List<AnimeEntity>();
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Page", out var page) &&
                page.TryGetProperty("media", out var mediaArr) &&
                mediaArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var media in mediaArr.EnumerateArray())
                {
                    var item = AniListMapper.MapMediaToAnimeEntity(media);
                    if (media.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var aniId))
                    {
                        _malToAniListMap[item.Id] = aniId;
                    }
                    list.Add(item);
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: SearchManga failed for query {Query}", query);
            return new List<AnimeEntity>();
        }
    }

    public async Task<AnimeEntity?> GetMangaDetailsAsync(int mangaId, CancellationToken ct = default)
    {
        const string queryByMal = """
        query ($malId: Int) {
          Media(idMal: $malId, type: MANGA) {
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
        """;

        try
        {
            using var doc = await ExecuteGraphQlAsync(queryByMal, new { malId = mangaId }, ct);
            if (doc != null &&
                doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Media", out var media) &&
                media.ValueKind == JsonValueKind.Object)
            {
                var item = AniListMapper.MapMediaToAnimeEntity(media);
                if (media.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var aniId))
                {
                    _malToAniListMap[item.Id] = aniId;
                }
                return item;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: GetMangaDetails failed for {MangaId}", mangaId);
        }

        return null;
    }
}
