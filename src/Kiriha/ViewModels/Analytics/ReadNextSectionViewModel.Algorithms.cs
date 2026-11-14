using System.Globalization;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public partial class ReadNextSectionViewModel
{
    private static ProfileTodoItem ToTodo(AnimeEntity item, string badge, string detail, string accent)
    {
        return new ProfileTodoItem
        {
            Title = item.Presentation.DisplayTitle,
            Subtitle = item.RussianTitle != null ? item.Title : null,
            Badge = badge,
            Detail = detail,
            PosterUrl = item.PosterSource ?? item.MainPictureUrl,
            Accent = accent
        };
    }

    private string FormatUpcomingDetail(AnimeEntity item)
    {
        var date = item.NextEpisodeAt?.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.CurrentCulture) ?? "-";
        if (string.Equals(item.Type, AppConstants.AnimeTypes.Movie, StringComparison.OrdinalIgnoreCase))
        {
            return $"{_localizer.GetLoc("anime.types.movie")} \u2022 {date}";
        }

        var nextEpisode = item.EpisodesAired > 0 ? item.EpisodesAired + 1 : 1;
        return $"{_localizer.GetLoc("anime.labels.series_only", nextEpisode)} \u2022 {date}";
    }

    private string FormatPlanDetail(AnimeEntity item)
    {
        if (item.Genres.Count > 0)
        {
            return string.Join(", ", item.Genres.Take(2).Select(LocalizeGenre));
        }

        var type = _localizer.GetLoc($"anime.types.{item.Type}");
        return type == $"anime.types.{item.Type}" ? item.Type : type;
    }

    private string LocalizeGenre(string genre)
    {
        string[] candidates =
        [
            genre.ToLowerInvariant().Replace(" ", string.Empty),
            ToResourceKey(genre),
            genre.ToLowerInvariant()
        ];

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = $"genres.{candidate}";
            var translated = _localizer.GetLoc(key);
            if (translated != key)
            {
                return translated;
            }
        }

        return genre;
    }

    private string ToResourceKey(string value)
    {
        List<char> chars = [with(capacity: value.Length)];
        var lastWasSeparator = false;
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Add(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                chars.Add('_');
                lastWasSeparator = true;
            }
        }

        return new string(chars.ToArray()).Trim('_');
    }

    private static bool IsCurrentOngoing(AnimeEntity item, DateTime now)
    {
        var isPublishing = string.Equals(item.StatusDetailed, "currently_airing", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(item.StatusDetailed, "currently_publishing", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(item.StatusDetailed, "ongoing", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(item.StatusDetailed, "publishing", StringComparison.OrdinalIgnoreCase);

        if (!isPublishing)
        {
            return false;
        }

        if (item.NextEpisodeAt.HasValue)
        {
            return item.NextEpisodeAt.Value >= now.AddDays(-14);
        }

        if (item.LastEpisodeAt.HasValue)
        {
            return item.LastEpisodeAt.Value >= now.AddDays(-21);
        }

        return true;
    }

    internal static int GetChaptersProgress(AnimeEntity item) =>
        item.ChaptersRead > 0 ? item.ChaptersRead : Math.Max(0, item.Progress);

    internal static int GetTotalChapters(AnimeEntity item) =>
        item.Chapters > 0 ? item.Chapters : Math.Max(0, item.TotalEpisodes);

    internal static int GetUnreadChaptersCount(AnimeEntity item)
    {
        var total = GetTotalChapters(item);
        if (total <= 0) return 0;
        var read = GetChaptersProgress(item);
        return Math.Max(0, total - read);
    }

    private static string DisplayTotal(AnimeEntity item)
    {
        var total = GetTotalChapters(item);
        return total > 0 ? total.ToString(CultureInfo.InvariantCulture) : "?";
    }

    private static double ParsedMeanScore(AnimeEntity item) =>
        double.TryParse(item.MeanScore, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
            ? score
            : 0;
}

