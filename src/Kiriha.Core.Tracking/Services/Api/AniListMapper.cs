using System.Text.Json;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Api;

public static class AniListMapper
{
    public static AnimeEntity MapMediaToAnimeEntity(JsonElement media)
    {
        var idMal = media.TryGetProperty("idMal", out var malProp) && malProp.ValueKind == JsonValueKind.Number && malProp.TryGetInt32(out var parsedMal)
            ? parsedMal
            : 0;

        var aniListId = media.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out var parsedId)
            ? parsedId
            : 0;

        // If MAL ID is available, use it as the primary Id so it seamlessly matches MAL and Shikimori.
        // Otherwise fallback to AniList ID.
        var primaryId = idMal > 0 ? idMal : aniListId;

        string title = string.Empty;
        if (media.TryGetProperty("title", out var titleObj) && titleObj.ValueKind == JsonValueKind.Object)
        {
            var english = titleObj.TryGetProperty("english", out var en) && en.ValueKind == JsonValueKind.String ? en.GetString() : null;
            var romaji = titleObj.TryGetProperty("romaji", out var ro) && ro.ValueKind == JsonValueKind.String ? ro.GetString() : null;
            var native = titleObj.TryGetProperty("native", out var na) && na.ValueKind == JsonValueKind.String ? na.GetString() : null;

            title = !string.IsNullOrEmpty(english) ? english : (!string.IsNullOrEmpty(romaji) ? romaji : native ?? string.Empty);
        }

        string? coverUrl = null;
        if (media.TryGetProperty("coverImage", out var coverObj) && coverObj.ValueKind == JsonValueKind.Object)
        {
            if (coverObj.TryGetProperty("extraLarge", out var xl) && xl.ValueKind == JsonValueKind.String)
                coverUrl = xl.GetString();
            else if (coverObj.TryGetProperty("large", out var lg) && lg.ValueKind == JsonValueKind.String)
                coverUrl = lg.GetString();
        }

        string? synopsis = media.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String
            ? descProp.GetString()
            : null;

        int totalEpisodes = media.TryGetProperty("episodes", out var epProp) && epProp.ValueKind == JsonValueKind.Number && epProp.TryGetInt32(out var eps)
            ? eps
            : 0;

        int chapters = media.TryGetProperty("chapters", out var chProp) && chProp.ValueKind == JsonValueKind.Number && chProp.TryGetInt32(out var ch)
            ? ch
            : 0;

        int volumes = media.TryGetProperty("volumes", out var volProp) && volProp.ValueKind == JsonValueKind.Number && volProp.TryGetInt32(out var vol)
            ? vol
            : 0;

        string? meanScore = null;
        if (media.TryGetProperty("averageScore", out var avgScoreProp) && avgScoreProp.ValueKind == JsonValueKind.Number && avgScoreProp.TryGetDouble(out var avgScore))
        {
            meanScore = (avgScore / 10.0).ToString("0.00");
        }

        int popularity = media.TryGetProperty("popularity", out var popProp) && popProp.ValueKind == JsonValueKind.Number && popProp.TryGetInt32(out var pop)
            ? pop
            : 0;

        string format = media.TryGetProperty("format", out var formatProp) && formatProp.ValueKind == JsonValueKind.String
            ? formatProp.GetString()?.ToUpperInvariant() ?? "TV"
            : "TV";

        string? status = media.TryGetProperty("status", out var stProp) && stProp.ValueKind == JsonValueKind.String
            ? stProp.GetString()
            : null;

        DateTime? startDate = null;
        if (media.TryGetProperty("startDate", out var dateObj) && dateObj.ValueKind == JsonValueKind.Object)
        {
            var y = dateObj.TryGetProperty("year", out var yr) && yr.ValueKind == JsonValueKind.Number && yr.TryGetInt32(out var yearVal) ? yearVal : 0;
            var m = dateObj.TryGetProperty("month", out var mo) && mo.ValueKind == JsonValueKind.Number && mo.TryGetInt32(out var moVal) ? moVal : 1;
            var d = dateObj.TryGetProperty("day", out var dy) && dy.ValueKind == JsonValueKind.Number && dy.TryGetInt32(out var dyVal) ? dyVal : 1;

            if (y > 1900)
            {
                try { startDate = new DateTime(y, Math.Clamp(m, 1, 12), Math.Clamp(d, 1, 28), 0, 0, 0, DateTimeKind.Utc); }
                catch { /* Ignore invalid date */ }
            }
        }

        var item = new AnimeEntity
        {
            Id = primaryId,
            Title = title,
            MainPictureUrl = coverUrl,
            Synopsis = synopsis,
            TotalEpisodes = totalEpisodes,
            Chapters = chapters,
            Volumes = volumes,
            MeanScore = meanScore,
            Popularity = popularity,
            AiringDate = startDate,
            StatusDetailed = status,
            Type = format
        };

        if (format is "MANGA" or "ONE_SHOT")
        {
            item.MediaKind = MediaKind.Manga;
        }
        else if (format is "NOVEL")
        {
            item.MediaKind = MediaKind.LightNovel;
        }
        else
        {
            item.MediaKind = MediaKind.Anime;
        }

        if (media.TryGetProperty("genres", out var genresProp) && genresProp.ValueKind == JsonValueKind.Array)
        {
            var genres = new List<string>();
            foreach (var g in genresProp.EnumerateArray())
            {
                var val = g.GetString();
                if (!string.IsNullOrEmpty(val)) genres.Add(val);
            }
            item.Genres = genres;
        }

        return item;
    }

    public static void MapEntryToUserStatus(JsonElement entry, AnimeEntity item)
    {
        if (entry.TryGetProperty("status", out var stProp) && stProp.ValueKind == JsonValueKind.String)
        {
            item.Status = StatusMapper.FromAniList(stProp.GetString());
        }

        if (entry.TryGetProperty("progress", out var prProp) && prProp.ValueKind == JsonValueKind.Number && prProp.TryGetInt32(out var pr))
        {
            item.Progress = pr;
            item.ChaptersRead = pr;
        }

        if (entry.TryGetProperty("progressVolumes", out var pvProp) && pvProp.ValueKind == JsonValueKind.Number && pvProp.TryGetInt32(out var pv))
        {
            item.VolumesRead = pv;
        }

        if (entry.TryGetProperty("score", out var scProp) && scProp.ValueKind == JsonValueKind.Number && scProp.TryGetDouble(out var sc))
        {
            int score = (int)Math.Round(sc);
            item.Score = score == 0 ? "-" : score.ToString();
        }

        if (entry.TryGetProperty("repeat", out var repProp) && repProp.ValueKind == JsonValueKind.Number && repProp.TryGetInt32(out var rep))
        {
            item.RewatchCount = rep;
            item.IsRewatching = rep > 0 && item.Status == UserAnimeStatus.Watching;
        }

        if (entry.TryGetProperty("notes", out var notesProp) && notesProp.ValueKind == JsonValueKind.String)
        {
            item.Notes = notesProp.GetString();
        }
    }
}
