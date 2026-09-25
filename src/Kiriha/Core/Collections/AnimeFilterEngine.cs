using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.Genres;
using Kiriha.Models;

using Kiriha.Core.Domain.Models.Formats;

namespace Kiriha.Core;

/// <summary>
/// Centralized engine for filtering and sorting AnimeEntity collections.
/// </summary>
public static class AnimeFilterEngine
{
    public static IEnumerable<AnimeEntity> ApplySearch(
        this IEnumerable<AnimeEntity> query,
        string? searchQuery,
        IReadOnlyCollection<string>? activeGenreKeys = null)
        => ApplySearch(query, searchQuery, activeGenreKeys, null);

    public static IEnumerable<AnimeEntity> ApplySearch(
        this IEnumerable<AnimeEntity> query,
        string? searchQuery,
        IReadOnlyCollection<string>? activeGenreKeys,
        IReadOnlyCollection<string>? activeFormatKeys)
    {
        var parsed = AnimeSearchQueryParser.Parse(searchQuery);

        HashSet<string>? requiredGenreKeys = null;
        if (activeGenreKeys != null && activeGenreKeys.Count > 0)
        {
            requiredGenreKeys = new HashSet<string>(activeGenreKeys, StringComparer.OrdinalIgnoreCase);
        }
        if (parsed.ExtractedGenreKeys.Count > 0)
        {
            requiredGenreKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in parsed.ExtractedGenreKeys)
            {
                requiredGenreKeys.Add(k);
            }
        }

        HashSet<string>? requiredFormatKeys = null;
        if (activeFormatKeys != null && activeFormatKeys.Count > 0)
        {
            requiredFormatKeys = new HashSet<string>(activeFormatKeys, StringComparer.OrdinalIgnoreCase);
        }
        if (parsed.ExtractedFormatKeys.Count > 0)
        {
            requiredFormatKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in parsed.ExtractedFormatKeys)
            {
                requiredFormatKeys.Add(k);
            }
        }

        bool hasTitleSearch = parsed.HasTitleSearch;
        string? titleSearch = hasTitleSearch ? parsed.TitleSearchText : null;

        if (requiredGenreKeys == null && requiredFormatKeys == null && !hasTitleSearch)
        {
            return query;
        }

        return query.Where(x =>
        {
            if (requiredFormatKeys != null && requiredFormatKeys.Count > 0)
            {
                bool matchesFormat = false;
                foreach (var req in requiredFormatKeys)
                {
                    if (FormatCatalog.MatchesFormat(x.Type, req))
                    {
                        matchesFormat = true;
                        break;
                    }
                }
                if (!matchesFormat) return false;
            }

            if (requiredGenreKeys != null && requiredGenreKeys.Count > 0)
            {
                if (x.Genres == null || x.Genres.Count == 0) return false;

                foreach (var req in requiredGenreKeys)
                {
                    bool hasGenre = x.Genres.Any(g =>
                        (GenreCatalog.TryFindGenre(g, out var def) && def != null && string.Equals(def.Key, req, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(GenreCatalog.NormalizeLookup(g), req, StringComparison.OrdinalIgnoreCase));

                    if (!hasGenre) return false;
                }
            }

            if (titleSearch != null)
            {
                return (x.Title?.Contains(titleSearch, StringComparison.OrdinalIgnoreCase) == true) ||
                       (x.RussianTitle?.Contains(titleSearch, StringComparison.OrdinalIgnoreCase) == true) ||
                       (x.EnglishTitle?.Contains(titleSearch, StringComparison.OrdinalIgnoreCase) == true) ||
                       (x.JapaneseTitle?.Contains(titleSearch, StringComparison.OrdinalIgnoreCase) == true);
            }

            return true;
        });
    }

    /// <summary>
    /// Filters the collection to show ONLY NSFW content if filterNsfw is true.
    /// This is an "Only NSFW 18+" mode, not a "Hide NSFW" filter.
    /// </summary>
    public static IEnumerable<AnimeEntity> ApplyNsfw(this IEnumerable<AnimeEntity> query, bool filterNsfw)
    {
        return filterNsfw ? query.Where(x => x.IsNsfw) : query.Where(x => !x.IsNsfw);
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
    private static readonly IEqualityComparer<(string, bool, bool)> KeyComparer =
        EqualityComparer<(string, bool, bool)>.Create(
            (x, y) => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && x.Item2 == y.Item2 && x.Item3 == y.Item3,
            obj => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1), obj.Item2, obj.Item3));

    private static readonly FrozenDictionary<(string, bool, bool), IComparer<AnimeEntity>> Comparers;

    static AnimeComparerFactory()
    {
        var comparers = new Dictionary<(string, bool, bool), IComparer<AnimeEntity>>(KeyComparer);
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

        Comparers = comparers.ToFrozenDictionary(KeyComparer);
    }

    public static IComparer<AnimeEntity> GetComparer(string? sortBy, bool isSeasonal, bool prioritizeNewEpisodes)
    {
        var key = (sortBy ?? string.Empty, isSeasonal, prioritizeNewEpisodes);
        return Comparers.TryGetValue(key, out var comparer)
            ? comparer
            : new AnimeEntityComparer(sortBy ?? string.Empty, isSeasonal, prioritizeNewEpisodes);
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

            if (string.Equals(_sortBy, AppConstants.Sorting.Score, StringComparison.OrdinalIgnoreCase))
                return CompareScore(x, y);
            if (string.Equals(_sortBy, "Progress", StringComparison.OrdinalIgnoreCase))
                return CompareProgress(x, y);
            if (string.Equals(_sortBy, AppConstants.Sorting.Date, StringComparison.OrdinalIgnoreCase))
                return CompareDate(x, y);
            if (string.Equals(_sortBy, AppConstants.Sorting.Popularity, StringComparison.OrdinalIgnoreCase))
                return ComparePopularity(x, y);
            if (string.Equals(_sortBy, "EnglishTitle", StringComparison.OrdinalIgnoreCase))
                return CompareEnglishTitle(x, y);
            if (string.Equals(_sortBy, AppConstants.Sorting.RussianTitle, StringComparison.OrdinalIgnoreCase))
                return CompareRussianTitle(x, y);
            return CompareTitle(x, y);
        }

        private int CompareScore(AnimeEntity x, AnimeEntity y)
        {
            double xScore = _isSeasonal ? x.MeanScoreValue : x.ScoreValue;
            double yScore = _isSeasonal ? y.MeanScoreValue : y.ScoreValue;
            int cmp = yScore.CompareTo(xScore);
            if (cmp != 0) return cmp;

            int popCmp = ComparePopularity(x, y);
            return popCmp != 0 ? popCmp : CompareTitle(x, y);
        }

        private static int CompareProgress(AnimeEntity x, AnimeEntity y)
        {
            int cmp = y.Presentation.ProgressValue.CompareTo(x.Presentation.ProgressValue);
            return cmp != 0 ? cmp : CompareTitle(x, y);
        }

        private int CompareDate(AnimeEntity x, AnimeEntity y)
        {
            if (_isSeasonal)
            {
                var xDateVal = x.AiringDate;
                var yDateVal = y.AiringDate;
                if (xDateVal.HasValue && yDateVal.HasValue)
                {
                    int cmp = xDateVal.GetValueOrDefault().CompareTo(yDateVal.GetValueOrDefault());
                    return cmp != 0 ? cmp : CompareTitle(x, y);
                }
                if (xDateVal.HasValue) return -1;
                if (yDateVal.HasValue) return 1;
                return CompareTitle(x, y);
            }

            DateTime xDate = x.AiringDate ?? DateTime.MinValue;
            DateTime yDate = y.AiringDate ?? DateTime.MinValue;
            int defaultCmp = yDate.CompareTo(xDate);
            return defaultCmp != 0 ? defaultCmp : CompareTitle(x, y);
        }

        private static int ComparePopularity(AnimeEntity x, AnimeEntity y)
        {
            int xp = x.Popularity <= 0 ? int.MaxValue : x.Popularity;
            int yp = y.Popularity <= 0 ? int.MaxValue : y.Popularity;
            int cmp = xp.CompareTo(yp);
            return cmp != 0 ? cmp : CompareTitle(x, y);
        }

        private static readonly StringComparer RuComparer = CultureInfo.GetCultureInfo("ru-RU").CompareInfo.GetStringComparer(CompareOptions.IgnoreCase);

        private static int CompareEnglishTitle(AnimeEntity x, AnimeEntity y)
        {
            string xt = !string.IsNullOrWhiteSpace(x.EnglishTitle) ? x.EnglishTitle.Trim() : (x.Title ?? string.Empty).Trim();
            string yt = !string.IsNullOrWhiteSpace(y.EnglishTitle) ? y.EnglishTitle.Trim() : (y.Title ?? string.Empty).Trim();
            int cmp = StringComparer.CurrentCultureIgnoreCase.Compare(xt, yt);
            return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
        }

        private static int CompareRussianTitle(AnimeEntity x, AnimeEntity y)
        {
            bool xHasRu = !string.IsNullOrWhiteSpace(x.RussianTitle);
            bool yHasRu = !string.IsNullOrWhiteSpace(y.RussianTitle);

            if (xHasRu && yHasRu)
            {
                int cmp = RuComparer.Compare(x.RussianTitle!.Trim(), y.RussianTitle!.Trim());
                return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
            }

            if (xHasRu && !yHasRu) return -1;
            if (!xHasRu && yHasRu) return 1;

            int titleCmp = RuComparer.Compare(x.Title ?? string.Empty, y.Title ?? string.Empty);
            return titleCmp != 0 ? titleCmp : x.Id.CompareTo(y.Id);
        }

        private static int CompareTitle(AnimeEntity x, AnimeEntity y)
        {
            int cmp = StringComparer.CurrentCultureIgnoreCase.Compare(x.Title ?? string.Empty, y.Title ?? string.Empty);
            return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
        }
    }
}
