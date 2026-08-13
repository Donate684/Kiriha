using System;
using Avalonia;

namespace Kiriha.Localization;

public static class LocalizationStore
{
    private static readonly string[] CommonPrefixes = { "anime.types.", "anime.seasons.", "anime.status.", "common.actions.", "common.status.", "filters.sort.", "genres.", "torrents.sort." };

    public static string Translate(string keyToUse)
    {
        if (Application.Current == null) return keyToUse;

        string lowerKey = keyToUse.ToLowerInvariant().Replace(" ", "");
        string snakeKey = PascalToSnake(keyToUse);

        if (Application.Current.Resources.TryGetValue($"l.{lowerKey}", out var translatedLower))
            return translatedLower?.ToString() ?? keyToUse;

        if (snakeKey != lowerKey && Application.Current.Resources.TryGetValue($"l.{snakeKey}", out var translatedSnake))
            return translatedSnake?.ToString() ?? keyToUse;

        foreach (var prefix in CommonPrefixes)
        {
            if (Application.Current.Resources.TryGetValue($"l.{prefix}{lowerKey}", out var translatedNested))
                return translatedNested?.ToString() ?? keyToUse;
            if (snakeKey != lowerKey && Application.Current.Resources.TryGetValue($"l.{prefix}{snakeKey}", out var translatedNestedSnake))
                return translatedNestedSnake?.ToString() ?? keyToUse;
        }

        var parts = keyToUse.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            string firstPartKey = parts[0].ToLowerInvariant();
            if (Application.Current.Resources.TryGetValue($"l.{firstPartKey}", out var firstTranslated))
                return $"{firstTranslated} {parts[1]}";
            if (Application.Current.Resources.TryGetValue($"l.anime.seasons.{firstPartKey}", out var firstTranslatedSeason))
                return $"{firstTranslatedSeason} {parts[1]}";
        }

        string key = keyToUse.Replace(" ", "");
        if (Application.Current.Resources.TryGetValue($"l.{key}", out var translatedExact))
            return translatedExact?.ToString() ?? keyToUse;

        if (!string.IsNullOrEmpty(keyToUse))
            return char.ToUpper(keyToUse[0]) + (keyToUse.Length > 1 ? keyToUse.Substring(1) : "");

        return keyToUse;
    }

    public static string PascalToSnake(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new System.Text.StringBuilder(input.Length + 4);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c) || c == '_' || c == '-')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                continue;
            }
            if (char.IsUpper(c) && i > 0 && sb.Length > 0 && sb[sb.Length - 1] != '_')
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
