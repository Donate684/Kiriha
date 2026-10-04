using System.Globalization;
using System.Text.Json;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Api;

public static class ShikiMapper
{
    public static AnimeEntity MapRateToAnimeEntity(
        JsonElement rateElement,
        string websiteRoot,
        Action<int, int>? idMappingCallback = null)
    {
        bool isManga;
        JsonElement mediaNode;
        bool hasMedia;

        if (rateElement.TryGetProperty("manga", out var mangaNode) && mangaNode.ValueKind == JsonValueKind.Object)
        {
            isManga = true;
            hasMedia = true;
            mediaNode = mangaNode;
        }
        else if (rateElement.TryGetProperty("anime", out var animeNode) && animeNode.ValueKind == JsonValueKind.Object)
        {
            isManga = false;
            hasMedia = true;
            mediaNode = animeNode;
        }
        else
        {
            var targetType = rateElement.GetOptionalString("target_type") ?? "Anime";
            isManga = targetType.Equals("Manga", StringComparison.OrdinalIgnoreCase);
            hasMedia = isManga
                ? rateElement.TryGetProperty("manga", out mediaNode) && mediaNode.ValueKind == JsonValueKind.Object
                : rateElement.TryGetProperty("anime", out mediaNode) && mediaNode.ValueKind == JsonValueKind.Object;
        }

        var targetId = rateElement.GetOptionalInt("target_id") ?? 0;
        int shikiId = targetId;
        int malId = 0;

        if (hasMedia)
        {
            var mediaId = mediaNode.GetOptionalInt("id");
            if (mediaId.HasValue && mediaId.Value > 0)
            {
                shikiId = mediaId.Value;
            }

            var parsedMal = mediaNode.GetOptionalInt("malId") ?? mediaNode.GetOptionalInt("mal_id");
            if (parsedMal.HasValue && parsedMal.Value > 0)
            {
                malId = parsedMal.Value;
            }
        }

        // Primary entity ID is MAL ID if available, otherwise Shikimori ID
        int primaryId = malId > 0 ? malId : shikiId;

        if (shikiId > 0 && idMappingCallback != null)
        {
            idMappingCallback(primaryId, shikiId);
        }

        string title;
        string? russianTitle = null;
        string? englishTitle = null;
        string? coverUrl = null;
        string? synopsis = null;
        int totalEpisodes = 0;
        int episodesAired = 0;
        int chapters = 0;
        int volumes = 0;
        string? meanScore = null;
        string? statusDetailed = null;
        string format = isManga ? "MANGA" : "TV";
        DateTime? airingDate = null;

        if (hasMedia)
        {
            englishTitle = mediaNode.GetOptionalString("name");
            russianTitle = mediaNode.GetOptionalString("russian");
            title = !string.IsNullOrWhiteSpace(englishTitle)
                ? englishTitle
                : (!string.IsNullOrWhiteSpace(russianTitle) ? russianTitle : $"ID {primaryId}");

            coverUrl = ResolveImageUrl(mediaNode, websiteRoot);
            synopsis = mediaNode.GetOptionalString("description");
            totalEpisodes = mediaNode.GetOptionalInt("episodes") ?? 0;
            episodesAired = mediaNode.GetOptionalInt("episodes_aired") ?? 0;
            chapters = mediaNode.GetOptionalInt("chapters") ?? 0;
            volumes = mediaNode.GetOptionalInt("volumes") ?? 0;
            statusDetailed = mediaNode.GetOptionalString("status");

            if (mediaNode.TryGetProperty("score", out var scoreProp))
            {
                if (scoreProp.ValueKind == JsonValueKind.Number && scoreProp.TryGetDouble(out var dScore))
                {
                    meanScore = dScore > 0 ? dScore.ToString("0.00", CultureInfo.InvariantCulture) : null;
                }
                else if (scoreProp.ValueKind == JsonValueKind.String)
                {
                    meanScore = scoreProp.GetString();
                }
            }

            var kind = mediaNode.GetOptionalString("kind");
            if (!string.IsNullOrEmpty(kind))
            {
                format = kind.ToUpperInvariant();
            }

            var airedOnStr = mediaNode.GetOptionalString("aired_on");
            if (!string.IsNullOrEmpty(airedOnStr) && DateTime.TryParse(airedOnStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var airedOnDate))
            {
                airingDate = airedOnDate;
            }
        }
        else
        {
            title = $"ID {primaryId}";
        }

        var item = new AnimeEntity
        {
            Id = primaryId,
            Title = title,
            RussianTitle = russianTitle,
            EnglishTitle = englishTitle,
            MainPictureUrl = coverUrl,
            Synopsis = synopsis,
            TotalEpisodes = totalEpisodes,
            EpisodesAired = episodesAired,
            Chapters = chapters,
            Volumes = volumes,
            MeanScore = meanScore,
            StatusDetailed = statusDetailed,
            Type = format,
            AiringDate = airingDate
        };

        if (format is "MANGA" or "ONE_SHOT" or "DOUJIN" or "MANHWA" or "MANHUA")
        {
            item.MediaKind = MediaKind.Manga;
        }
        else if (format is "LIGHT_NOVEL" or "NOVEL")
        {
            item.MediaKind = MediaKind.LightNovel;
        }
        else
        {
            item.MediaKind = MediaKind.Anime;
        }

        if (!string.IsNullOrEmpty(englishTitle)) item.AlternativeTitles.Add(englishTitle);
        if (!string.IsNullOrEmpty(russianTitle)) item.AlternativeTitles.Add(russianTitle);

        MapUserRateStatus(rateElement, item);

        return item;
    }

    public static void MapUserRateStatus(JsonElement rateElement, AnimeEntity item)
    {
        var rawStatus = rateElement.GetOptionalString("status");
        item.Status = StatusMapper.FromShiki(rawStatus);

        if (string.Equals(rawStatus, "rewatching", StringComparison.OrdinalIgnoreCase))
        {
            item.IsRewatching = true;
        }

        item.Progress = rateElement.GetOptionalInt("episodes") ?? 0;
        item.ChaptersRead = rateElement.GetOptionalInt("chapters") ?? 0;
        item.VolumesRead = rateElement.GetOptionalInt("volumes") ?? 0;

        var score = rateElement.GetOptionalInt("score");
        item.Score = score.HasValue && score.Value > 0 ? score.Value.ToString() : "-";

        item.RewatchCount = rateElement.GetOptionalInt("rewatches") ?? 0;
        item.Notes = rateElement.GetOptionalString("text");

        var createdAtStr = rateElement.GetOptionalString("created_at");
        if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt))
        {
            item.DateStarted ??= createdAt;
        }

        var updatedAtStr = rateElement.GetOptionalString("updated_at");
        if (!string.IsNullOrEmpty(updatedAtStr) && DateTime.TryParse(updatedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var updatedAt))
        {
            if (item.Status == UserAnimeStatus.Completed)
            {
                item.DateCompleted ??= updatedAt;
            }
        }
    }

    private static string? ResolveImageUrl(JsonElement mediaNode, string websiteRoot)
    {
        if (!mediaNode.TryGetProperty("image", out var img) || img.ValueKind != JsonValueKind.Object)
            return null;

        var url = img.GetOptionalString("original") ?? img.GetOptionalString("preview");
        if (string.IsNullOrEmpty(url) || AnimeEntity.IsMissingPosterUrl(url)) return null;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return url;
        }

        var root = websiteRoot.TrimEnd('/');
        var relative = url.StartsWith('/') ? url : "/" + url;
        return root + relative;
    }

    private static string? GetOptionalString(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetOptionalInt(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var num)) return num;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var parsed)) return parsed;
        return null;
    }
}
