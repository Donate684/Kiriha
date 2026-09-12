using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Constants;
using Kiriha.Utils.Collections;

namespace Kiriha.Utils.Parsing;

public static class AnimeStringHelper
{
    private static readonly FrozenDictionary<string, string> RomanNumerals = new KeyValuePair<string, string>[]
    {
        new("i", "1"), new("ii", "2"), new("iii", "3"), new("iv", "4"), new("v", "5"),
        new("vi", "6"), new("vii", "7"), new("viii", "8"), new("ix", "9"), new("x", "10"),
        new("xi", "11"), new("xii", "12"), new("xiii", "13"), new("xiv", "14"), new("xv", "15")
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> Ordinals = new KeyValuePair<string, string>[]
    {
        new("first", "1st"), new("second", "2nd"), new("third", "3rd"),
        new("fourth", "4th"), new("fifth", "5th"), new("sixth", "6th"),
        new("seventh", "7th"), new("eighth", "8th"), new("ninth", "9th")
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> SeasonsMap = new KeyValuePair<string, string>[]
    {
        new("1st season", "1"), new("season 1", "1"), new("series 1", "1"), new("s1", "1"),
        new("2nd season", "2"), new("season 2", "2"), new("series 2", "2"), new("s2", "2"),
        new("3rd season", "3"), new("season 3", "3"), new("series 3", "3"), new("s3", "3"),
        new("4th season", "4"), new("season 4", "4"), new("series 4", "4"), new("s4", "4"),
        new("5th season", "5"), new("season 5", "5"), new("series 5", "5"), new("s5", "5"),
        new("6th season", "6"), new("season 6", "6"), new("series 6", "6"), new("s6", "6")
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> GenericReplacements = new KeyValuePair<string, string>[]
    {
        new("&", "and"),
        new("the animation", ""),
        new("the", ""),
        new("episode", ""),
        new("oad", "ova"),
        new("oav", "ova"),
        new("specials", "sp"),
        new("special", "sp"),
        new("(tv)", "")
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> WapuroMap = new KeyValuePair<string, string>[]
    {
        new("wa", "ha"), new("e", "he"), new("o", "wo")
    }.ToFrozenDictionary();

    // Pre-compiled Regexes for maximum performance
    private static readonly Regex SpacesRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex RomanRegex;
    private static readonly Regex OrdinalRegex;
    private static readonly Regex SeasonRegex;
    private static readonly Regex GenericRegex;
    private static readonly Regex WapuroRegex;

    static AnimeStringHelper()
    {
        RomanRegex = BuildGroupRegex(RomanNumerals.Keys);
        OrdinalRegex = BuildGroupRegex(Ordinals.Keys);
        SeasonRegex = BuildGroupRegex(SeasonsMap.Keys);
        GenericRegex = BuildGroupRegex(GenericReplacements.Keys);
        WapuroRegex = BuildGroupRegex(WapuroMap.Keys);
    }

    private static Regex BuildGroupRegex(IEnumerable<string> patterns)
    {
        // Sort by length descending to match longer phrases first (e.g., "1st season" before "1")
        var escaped = patterns.OrderByDescending(p => p.Length).Select(Regex.Escape);
        string pattern = $@"(?<=^|[^a-z0-9])({string.Join("|", escaped)})(?=[^a-z0-9]|$)";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    // Hot-path memoization. MappingService normalizes the same title (and each
    // of x.Title / x.EnglishTitle / x.RussianTitle) repeatedly during one
    // matching attempt; Anisthesia strategies normalize the same window title
    // every detection tick. Capped at 2048 entries — way more than the unique
    // title set of any realistic session.
    private static readonly LruStringMemoizer<string> _normalizeCache = new(2048);

    public static string Normalize(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        return _normalizeCache.GetOrAdd(title, static key => NormalizeCore(key));
    }

    private static string NormalizeCore(string title)
    {
        // 1. Initial cleanup and lower case
        string result = title.Trim().ToLowerInvariant();

        // 1.5 Unicode Normalization
        result = result.Normalize(NormalizationForm.FormKD);
        var sbNorm = new StringBuilder(result.Length);
        foreach (char c in result)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sbNorm.Append(c);
        }
        result = sbNorm.ToString().Normalize(NormalizationForm.FormKC);

        // 2. Specific character replacements
        result = result.Replace("ō", "ou").Replace("ū", "uu")
                       .Replace("@", "a").Replace("×", "x").Replace("꞉", ":").Replace(AppConstants.Parsing.CyrillicChe, "x");

        // 3. Batch replacements using compiled regexes
        result = ApplyGroupReplacements(result, RomanRegex, RomanNumerals);
        result = ApplyGroupReplacements(result, OrdinalRegex, Ordinals);
        result = ApplyGroupReplacements(result, SeasonRegex, SeasonsMap);
        result = ApplyGroupReplacements(result, WapuroRegex, WapuroMap);
        result = ApplyGroupReplacements(result, GenericRegex, GenericReplacements);

        // 4. Punctuation removal and simplification using StringBuilder
        var sb = new StringBuilder(result.Length);
        foreach (char c in result)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else
                sb.Append(' ');
        }

        return SpacesRegex.Replace(sb.ToString(), " ").Trim();
    }

    private static string ApplyGroupReplacements(string input, Regex regex, IReadOnlyDictionary<string, string> map)
    {
        return regex.Replace(input, match =>
            map.TryGetValue(match.Value.ToLowerInvariant(), out var replacement) ? replacement : match.Value);
    }

    private static readonly Regex ShikiTagsRegex = new(@"\[\w+(=[^\]]+)?\](.*?)\[/\w+\]", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex GenericTagsRegex = new(@"\[/?\w+(=[^\]]+)?\]", RegexOptions.Compiled);

    public static string CleanShikiDescription(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Handle Shikimori's placeholder for blocked titles
        if (input.Contains(AppConstants.Parsing.BlockedByRoskomnadzor)) return string.Empty;

        // 1. First replace [tag=val]Content[/tag] with Content
        string result = ShikiTagsRegex.Replace(input, "$2");

        // 2. Then remove any stray tags like [i] or [b] that might be left
        result = GenericTagsRegex.Replace(result, "");

        return result.Trim();
    }
}
