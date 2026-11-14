using Avalonia;
using Kiriha.Core.Domain.Extensions;

namespace Kiriha.Localization;

public static class LocalizationStore
{
    private static readonly string[] CommonPrefixes = { "anime.types.", "anime.seasons.", "anime.status.", "common.actions.", "common.status.", "filters.sort.", "genres.", "torrents.sort." };

    public static string Translate(string keyToUse)
    {
        if (keyToUse.StartsWith("l.", StringComparison.OrdinalIgnoreCase))
            keyToUse = keyToUse[2..];

        var snapshot = Kiriha.Services.Data.Metadata.LocalizationService.ActiveResources;
        if (snapshot.Count > 0)
        {
            if (snapshot.TryGetValue(keyToUse, out var directSnap))
                return directSnap;

            string lowerSnap = keyToUse.ToLowerInvariant().Replace(" ", "");
            if (snapshot.TryGetValue(lowerSnap, out var lowerVal))
                return lowerVal;

            string snakeSnap = PascalToSnake(keyToUse);
            if (snakeSnap != lowerSnap && snapshot.TryGetValue(snakeSnap, out var snakeVal))
                return snakeVal;

            foreach (var prefix in CommonPrefixes)
            {
                if (snapshot.TryGetValue($"{prefix}{lowerSnap}", out var nestedLower))
                    return nestedLower;
                if (snakeSnap != lowerSnap && snapshot.TryGetValue($"{prefix}{snakeSnap}", out var nestedSnake))
                    return nestedSnake;
            }
        }

        if (Application.Current is null || !Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) return keyToUse;

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

        return keyToUse.UppercaseFirst();
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
