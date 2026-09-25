using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Formats;

namespace Kiriha.Core.Domain.Models.Genres;

public sealed record ParsedAnimeSearchQuery(
    string RawQuery,
    string TitleSearchText,
    IReadOnlyList<GenreDefinition> ExtractedGenres,
    IReadOnlySet<string> ExtractedGenreKeys,
    IReadOnlyList<FormatDefinition> ExtractedFormats,
    IReadOnlySet<string> ExtractedFormatKeys)
{
    public bool HasTitleSearch => !string.IsNullOrWhiteSpace(TitleSearchText);
    public bool HasGenreFilter => ExtractedGenres.Count > 0;
    public bool HasFormatFilter => ExtractedFormats.Count > 0;
    public bool IsEmpty => !HasTitleSearch && !HasGenreFilter && !HasFormatFilter;

    public static readonly ParsedAnimeSearchQuery Empty = new(
        string.Empty,
        string.Empty,
        [],
        FrozenSet<string>.Empty,
        [],
        FrozenSet<string>.Empty);
}

public static class AnimeSearchQueryParser
{
    private static readonly string[] GenreTagPrefixes = AppConstants.Genres.TagPrefixes;
    private static readonly string[] FormatTagPrefixes = AppConstants.AnimeTypes.TagPrefixes;

    public static ParsedAnimeSearchQuery Parse(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return ParsedAnimeSearchQuery.Empty;
        }

        var trimmed = rawQuery.Trim();
        var extractedGenres = new List<GenreDefinition>();
        var genreKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extractedFormats = new List<FormatDefinition>();
        var formatKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titleTokens = new List<string>();

        // 1. Check if the whole query is an exact format (e.g. "фильм", "movie", "ova")
        if (FormatCatalog.TryFindFormat(trimmed, out var fullMatchFormat) && fullMatchFormat != null)
        {
            extractedFormats.Add(fullMatchFormat);
            formatKeys.Add(fullMatchFormat.Key);
            return new ParsedAnimeSearchQuery(
                trimmed,
                string.Empty,
                extractedGenres,
                genreKeys.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                extractedFormats,
                formatKeys.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
        }

        // 2. Check if the whole query is an exact genre (including multi-word ones like "slice of life" or "научная фантастика")
        if (GenreCatalog.TryFindGenre(trimmed, out var fullMatchGenre) && fullMatchGenre != null)
        {
            extractedGenres.Add(fullMatchGenre);
            genreKeys.Add(fullMatchGenre.Key);
            return new ParsedAnimeSearchQuery(
                trimmed,
                string.Empty,
                extractedGenres,
                genreKeys.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                extractedFormats,
                formatKeys.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
        }

        // 3. Tokenize by extracting quoted phrases first, then words
        var tokens = ExtractTokens(trimmed);

        foreach (var token in tokens)
        {
            if (token.IsQuoted)
            {
                titleTokens.Add(token.Text);
                continue;
            }

            var text = token.Text;

            // Prefix #... (can be format or genre, e.g. #movie or #drama)
            if (text.StartsWith('#') && text.Length > 1)
            {
                var candidate = text[1..];
                if (FormatCatalog.TryFindFormat(candidate, out var f) && f != null)
                {
                    if (formatKeys.Add(f.Key)) extractedFormats.Add(f);
                    continue;
                }
                if (GenreCatalog.TryFindGenre(candidate, out var g) && g != null)
                {
                    if (genreKeys.Add(g.Key)) extractedGenres.Add(g);
                    continue;
                }
            }

            // Prefix format:... / type:...
            bool handledPrefix = false;
            foreach (var p in FormatTagPrefixes)
            {
                if (text.StartsWith(p, StringComparison.OrdinalIgnoreCase) && text.Length > p.Length)
                {
                    var candidate = text[p.Length..];
                    if (FormatCatalog.TryFindFormat(candidate, out var f) && f != null)
                    {
                        if (formatKeys.Add(f.Key)) extractedFormats.Add(f);
                        handledPrefix = true;
                        break;
                    }
                }
            }
            if (handledPrefix) continue;

            // Prefix tag:... / genre:...
            foreach (var p in GenreTagPrefixes)
            {
                if (text.StartsWith(p, StringComparison.OrdinalIgnoreCase) && text.Length > p.Length)
                {
                    var candidate = text[p.Length..];
                    if (GenreCatalog.TryFindGenre(candidate, out var g) && g != null)
                    {
                        if (genreKeys.Add(g.Key)) extractedGenres.Add(g);
                        handledPrefix = true;
                        break;
                    }
                }
            }
            if (handledPrefix) continue;

            // Check if token matches format (e.g. "фильм", "ova", "ona", "movie")
            if (FormatCatalog.TryFindFormat(text, out var directFormat) && directFormat != null)
            {
                if (formatKeys.Add(directFormat.Key)) extractedFormats.Add(directFormat);
                continue;
            }

            // Check if token matches genre (e.g. "эччи", "drama", "комедия", "ecchi")
            if (GenreCatalog.TryFindGenre(text, out var directGenre) && directGenre != null)
            {
                if (genreKeys.Add(directGenre.Key)) extractedGenres.Add(directGenre);
                continue;
            }

            titleTokens.Add(text);
        }

        var titleSearch = string.Join(" ", titleTokens).Trim();

        return new ParsedAnimeSearchQuery(
            trimmed,
            titleSearch,
            extractedGenres,
            genreKeys.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            extractedFormats,
            formatKeys.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
    }

    private readonly record struct QueryToken(string Text, bool IsQuoted);

    private static List<QueryToken> ExtractTokens(string input)
    {
        var result = new List<QueryToken>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"')
            {
                if (inQuotes)
                {
                    if (sb.Length > 0)
                    {
                        result.Add(new QueryToken(sb.ToString(), true));
                        sb.Clear();
                    }
                    inQuotes = false;
                }
                else
                {
                    if (sb.Length > 0)
                    {
                        result.Add(new QueryToken(sb.ToString(), false));
                        sb.Clear();
                    }
                    inQuotes = true;
                }
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    result.Add(new QueryToken(sb.ToString(), false));
                    sb.Clear();
                }
                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0)
        {
            result.Add(new QueryToken(sb.ToString(), inQuotes));
        }

        return result;
    }
}
