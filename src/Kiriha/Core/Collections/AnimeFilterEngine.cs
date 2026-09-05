using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core;
using System;
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

    public static IEnumerable<AnimeEntity> ApplySorting(this IEnumerable<AnimeEntity> query, string? sortBy, bool isSeasonal = false)
    {
        return sortBy switch
        {
            "Score" => query.OrderByDescending(x => isSeasonal ? x.MeanScoreValue : x.ScoreValue),
            "Progress" => query.OrderByDescending(x => x.Presentation.ProgressValue),
            "Date" => query.OrderByDescending(x => x.AiringDate ?? DateTime.MinValue),
            "Popularity" => query.OrderBy(x => x.Popularity <= 0 ? int.MaxValue : x.Popularity),
            "EnglishTitle" => query.OrderBy(x => !string.IsNullOrEmpty(x.EnglishTitle) ? x.EnglishTitle : x.Title),
            "RussianTitle" => query.OrderBy(x => !string.IsNullOrEmpty(x.RussianTitle) ? x.RussianTitle : x.Title),
            "Title" => query.OrderBy(x => x.Title),
            _ => query.OrderBy(x => x.Title)
        };
    }

    private static double ParseScoreToDouble(string? score) => AnimeEntity.ParseScoreToDouble(score);
}
