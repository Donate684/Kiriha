using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Core;

/// <summary>
/// Centralized engine for filtering and sorting AnimeEntity collections.
/// </summary>
public static class AnimeFilterEngine
{
    public static IEnumerable<AnimeEntity> ApplySearch(this IEnumerable<AnimeEntity> query, string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery)) return query;

        return query.Where(x =>
            (x.Title?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (x.RussianTitle?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (x.EnglishTitle?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (x.JapaneseTitle?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true));
    }

    /// <summary>
    /// Filters the collection to show ONLY NSFW content if filterNsfw is true.
    /// This is an "Only NSFW 18+" mode, not a "Hide NSFW" filter.
    /// </summary>
    public static IEnumerable<AnimeEntity> ApplyNsfw(this IEnumerable<AnimeEntity> query, bool filterNsfw)
    {
        if (filterNsfw)
        {
            return query.Where(x =>
                string.Equals(x.Rating, "rx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Nsfw, "black", StringComparison.OrdinalIgnoreCase) ||
                x.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase)));
        }

        return query.Where(x =>
            !string.Equals(x.Rating, "rx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(x.Nsfw, "black", StringComparison.OrdinalIgnoreCase) &&
            !x.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase)));
    }

    public static IEnumerable<AnimeEntity> ApplySorting(this IEnumerable<AnimeEntity> query, string? sortBy, bool isSeasonal = false, bool prioritizeNewEpisodes = false)
    {
        var list = query as List<AnimeEntity> ?? query.ToList();
        list.SortInPlace(sortBy, isSeasonal, prioritizeNewEpisodes);
        return list;
    }

    public static List<AnimeEntity> SortInPlace(this List<AnimeEntity> list, string? sortBy, bool isSeasonal = false, bool prioritizeNewEpisodes = false)
    {
        var comparer = AnimeComparerFactory.GetComparer(sortBy, isSeasonal, prioritizeNewEpisodes);
        list.Sort(comparer);
        return list;
    }
}

internal static class AnimeComparerFactory
{
    private static readonly FrozenDictionary<(string, bool, bool), IComparer<AnimeEntity>> Comparers;

    static AnimeComparerFactory()
    {
        var comparers = new Dictionary<(string, bool, bool), IComparer<AnimeEntity>>(StringTupleComparer.Instance);
        string[] sortOptions = ["Title", "RussianTitle", "EnglishTitle", "Score", "Progress", "Date", "Popularity", ""];
        bool[] bools = [false, true];

        foreach (var opt in sortOptions)
        {
            foreach (var isSeasonal in bools)
            {
                foreach (var prioritizeNew in bools)
                {
                    comparers[(opt, isSeasonal, prioritizeNew)] = new AnimeEntityComparer(opt, isSeasonal, prioritizeNew);
                }
            }
        }

        Comparers = comparers.ToFrozenDictionary(StringTupleComparer.Instance);
    }

    public static IComparer<AnimeEntity> GetComparer(string? sortBy, bool isSeasonal, bool prioritizeNewEpisodes)
    {
        var key = (sortBy ?? string.Empty, isSeasonal, prioritizeNewEpisodes);
        return Comparers.TryGetValue(key, out var comparer)
            ? comparer
            : new AnimeEntityComparer(sortBy ?? string.Empty, isSeasonal, prioritizeNewEpisodes);
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string, bool, bool)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, bool, bool) x, (string, bool, bool) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            x.Item2 == y.Item2 &&
            x.Item3 == y.Item3;

        public int GetHashCode((string, bool, bool) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1), obj.Item2, obj.Item3);
    }

    private sealed class AnimeEntityComparer : IComparer<AnimeEntity>
    {
        private readonly string _sortBy;
        private readonly bool _isSeasonal;
        private readonly bool _prioritizeNewEpisodes;

        public AnimeEntityComparer(string sortBy, bool isSeasonal, bool prioritizeNewEpisodes)
        {
            _sortBy = sortBy;
            _isSeasonal = isSeasonal;
            _prioritizeNewEpisodes = prioritizeNewEpisodes;
        }

        public int Compare(AnimeEntity? x, AnimeEntity? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;

            if (_prioritizeNewEpisodes)
            {
                int badgeCompare = y.Presentation.HasNewEpisodeBadge.CompareTo(x.Presentation.HasNewEpisodeBadge);
                if (badgeCompare != 0) return badgeCompare;
            }

            return _sortBy switch
            {
                "Score" => CompareScore(x, y),
                "Progress" => CompareProgress(x, y),
                "Date" => CompareDate(x, y),
                "Popularity" => ComparePopularity(x, y),
                "EnglishTitle" => CompareEnglishTitle(x, y),
                "RussianTitle" => CompareRussianTitle(x, y),
                "Title" => CompareTitle(x, y),
                _ => CompareTitle(x, y)
            };
        }

        private int CompareScore(AnimeEntity x, AnimeEntity y)
        {
            double xScore = _isSeasonal ? x.MeanScoreValue : x.ScoreValue;
            double yScore = _isSeasonal ? y.MeanScoreValue : y.ScoreValue;
            int cmp = yScore.CompareTo(xScore);
            return cmp != 0 ? cmp : CompareTitle(x, y);
        }

        private static int CompareProgress(AnimeEntity x, AnimeEntity y)
        {
            int cmp = y.Presentation.ProgressValue.CompareTo(x.Presentation.ProgressValue);
            return cmp != 0 ? cmp : CompareTitle(x, y);
        }

        private static int CompareDate(AnimeEntity x, AnimeEntity y)
        {
            DateTime xDate = x.AiringDate ?? DateTime.MinValue;
            DateTime yDate = y.AiringDate ?? DateTime.MinValue;
            int cmp = yDate.CompareTo(xDate);
            return cmp != 0 ? cmp : CompareTitle(x, y);
        }

        private static int ComparePopularity(AnimeEntity x, AnimeEntity y)
        {
            int xp = x.Popularity <= 0 ? int.MaxValue : x.Popularity;
            int yp = y.Popularity <= 0 ? int.MaxValue : y.Popularity;
            int cmp = xp.CompareTo(yp);
            return cmp != 0 ? cmp : CompareTitle(x, y);
        }

        private static int CompareEnglishTitle(AnimeEntity x, AnimeEntity y)
        {
            string xt = !string.IsNullOrEmpty(x.EnglishTitle) ? x.EnglishTitle : (x.Title ?? string.Empty);
            string yt = !string.IsNullOrEmpty(y.EnglishTitle) ? y.EnglishTitle : (y.Title ?? string.Empty);
            int cmp = StringComparer.CurrentCulture.Compare(xt, yt);
            return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
        }

        private static int CompareRussianTitle(AnimeEntity x, AnimeEntity y)
        {
            string xt = !string.IsNullOrEmpty(x.RussianTitle) ? x.RussianTitle : (x.Title ?? string.Empty);
            string yt = !string.IsNullOrEmpty(y.RussianTitle) ? y.RussianTitle : (y.Title ?? string.Empty);
            int cmp = StringComparer.CurrentCulture.Compare(xt, yt);
            return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
        }

        private static int CompareTitle(AnimeEntity x, AnimeEntity y)
        {
            int cmp = StringComparer.CurrentCulture.Compare(x.Title ?? string.Empty, y.Title ?? string.Empty);
            return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
        }
    }
}
