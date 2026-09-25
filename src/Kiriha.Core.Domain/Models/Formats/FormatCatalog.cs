using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kiriha.Core.Domain.Constants;

namespace Kiriha.Core.Domain.Models.Formats;

public static class FormatCatalog
{
    private static readonly IReadOnlyList<FormatDefinition> Definitions = AppConstants.AnimeTypes.Definitions;

    private static readonly FrozenDictionary<string, FormatDefinition> LookupMap;
    private static readonly FrozenDictionary<string, FormatDefinition> ByKeyMap;

    static FormatCatalog()
    {
        var lookup = new Dictionary<string, FormatDefinition>(StringComparer.OrdinalIgnoreCase);
        var byKey = new Dictionary<string, FormatDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in Definitions)
        {
            byKey[def.Key] = def;

            Register(lookup, def.Key, def);
            Register(lookup, def.EnglishName, def);
            Register(lookup, def.RussianName, def);

            if (def.Aliases != null)
            {
                foreach (var alias in def.Aliases)
                {
                    Register(lookup, alias, def);
                }
            }
        }

        LookupMap = lookup.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        ByKeyMap = byKey.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static void Register(Dictionary<string, FormatDefinition> map, string key, FormatDefinition def)
    {
        var normalized = NormalizeLookup(key);
        if (normalized.Length > 0 && !map.ContainsKey(normalized))
        {
            map[normalized] = def;
        }
    }

    public static IReadOnlyList<FormatDefinition> All => Definitions;

    public static string NormalizeLookup(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var span = text.AsSpan().Trim();
        var sb = new StringBuilder(span.Length);

        foreach (var ch in span)
        {
            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == '.' || ch == '/' ||
                ch == '\\' || ch == '#' || ch == ':' || ch == '(' || ch == ')' || ch == '"' || ch == '\'')
            {
                continue;
            }

            char lower = char.ToLowerInvariant(ch);
            if (lower == 'ё') lower = 'е';
            sb.Append(lower);
        }

        return sb.ToString();
    }

    public static bool TryFindFormat(string? queryOrToken, out FormatDefinition? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(queryOrToken)) return false;

        var normalized = NormalizeLookup(queryOrToken);
        if (normalized.Length == 0) return false;

        return LookupMap.TryGetValue(normalized, out definition);
    }

    public static FormatDefinition? FindByKey(string key)
    {
        return ByKeyMap.TryGetValue(key, out var def) ? def : null;
    }

    public static IReadOnlyList<FormatDefinition> SearchFormats(string? query, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(query)) return Definitions.Take(maxResults).ToList();

        var normalized = NormalizeLookup(query);
        if (normalized.Length == 0) return Definitions.Take(maxResults).ToList();

        var exact = new List<FormatDefinition>();
        var startsWith = new List<FormatDefinition>();
        var contains = new List<FormatDefinition>();

        foreach (var def in Definitions)
        {
            var normKey = NormalizeLookup(def.Key);
            var normEn = NormalizeLookup(def.EnglishName);
            var normRu = NormalizeLookup(def.RussianName);

            if (normKey == normalized || normEn == normalized || normRu == normalized)
            {
                exact.Add(def);
                continue;
            }

            if (normRu.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                normEn.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                normKey.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                startsWith.Add(def);
                continue;
            }

            bool aliasPrefix = def.Aliases != null && def.Aliases.Any(a => NormalizeLookup(a).StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
            if (aliasPrefix)
            {
                startsWith.Add(def);
                continue;
            }

            if (normRu.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                normEn.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                contains.Add(def);
            }
        }

        return exact.Concat(startsWith).Concat(contains).DistinctBy(x => x.Key).Take(maxResults).ToList();
    }

    public static bool MatchesFormat(string? itemType, string formatKey)
    {
        if (string.IsNullOrWhiteSpace(itemType)) return false;
        var norm = itemType.Trim().ToLowerInvariant();
        if (string.Equals(norm, formatKey, StringComparison.OrdinalIgnoreCase)) return true;

        if (string.Equals(formatKey, AppConstants.AnimeTypes.Special, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(norm, AppConstants.AnimeTypes.TvSpecial, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
