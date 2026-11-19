namespace Kiriha.Core.Domain.Utils.Parsing;

public static partial class AnimeReleaseParser
{
    private static string ProcessBrackets(
        string text,
        ref string releaseGroup,
        ref string resolution,
        ref string releaseType,
        ref int? season,
        ref int? year,
        ref string episode,
        ref bool isBatch)
    {
        var result = new System.Text.StringBuilder(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (c is '[' or '(')
            {
                char closeChar = c == '[' ? ']' : ')';
                int closeIndex = text.IndexOf(closeChar, i + 1);

                if (closeIndex > i + 1)
                {
                    string inner = text[(i + 1)..closeIndex].Trim();
                    bool shouldRemove = false;

                    // Разрешение
                    var res = ExtractResolutionFromInner(inner);
                    if (res != null)
                    {
                        if (string.IsNullOrEmpty(resolution)) resolution = res;
                        shouldRemove = true;
                    }

                    // Батч-маркеры
                    if (inner.Contains("batch", StringComparison.OrdinalIgnoreCase) ||
                        inner.Contains("complete", StringComparison.OrdinalIgnoreCase))
                    {
                        isBatch = true;
                        shouldRemove = true;
                    }

                    // Диапазон серий [01-24]
                    int hyphenIdx = inner.IndexOf('-');
                    if (hyphenIdx < 0) hyphenIdx = inner.IndexOf('~');
                    if (hyphenIdx > 0 && hyphenIdx < inner.Length - 1)
                    {
                        var leftPart = inner[..hyphenIdx].Trim();
                        var rightPart = inner[(hyphenIdx + 1)..].Trim();
                        if (int.TryParse(leftPart, out int startVal) && int.TryParse(rightPart, out int endVal))
                        {
                            if (startVal >= 1960 && endVal >= 1960 && startVal <= 2099 && endVal <= 2099)
                            {
                                // Multi-year range like (2023-2024): record start year and remove bracket without assigning episode/batch
                                year ??= startVal;
                                shouldRemove = true;
                            }
                            else
                            {
                                episode = $"{leftPart}-{rightPart}";
                                isBatch = true;
                                shouldRemove = true;
                            }
                        }
                    }

                    // Single year like (2023)
                    if (!shouldRemove && int.TryParse(inner, out int singleYear) && singleYear >= 1960 && singleYear <= 2099)
                    {
                        year ??= singleYear;
                        shouldRemove = true;
                    }

                    // Прогресс [05 из 12]
                    int izIdx = inner.IndexOf(" \u0438\u0437 ", StringComparison.OrdinalIgnoreCase);
                    if (izIdx < 0) izIdx = inner.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
                    if (izIdx > 0)
                    {
                        var leftPart = inner[..izIdx].Trim();
                        if (int.TryParse(leftPart, out _))
                        {
                            episode = leftPart;
                            shouldRemove = true;
                        }
                    }

                    // ТВ-сезоны [ТВ-1], [TV-2]
                    if (inner.StartsWith("\u0422\u0412-", StringComparison.OrdinalIgnoreCase) ||
                        inner.StartsWith("TV-", StringComparison.OrdinalIgnoreCase) ||
                        inner.StartsWith("\u0422\u0412 ", StringComparison.OrdinalIgnoreCase) ||
                        inner.StartsWith("TV ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(inner[3..].Trim(), out int parsedSeason))
                        {
                            season ??= parsedSeason;
                            shouldRemove = true;
                        }
                    }

                    // Season в скобках: (Season 1), (2nd Season)
                    var matchSeasonBracket = ExplicitSeasonRegex().Match(inner);
                    if (matchSeasonBracket.Success)
                    {
                        if (int.TryParse(matchSeasonBracket.Groups["season"].Value, out int parsedSeason))
                        {
                            season ??= parsedSeason;
                            shouldRemove = true;
                        }
                    }
                    else if (inner.StartsWith("Season ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(inner[7..].Trim(), out int parsedSeason))
                        {
                            season ??= parsedSeason;
                            shouldRemove = true;
                        }
                    }

                    // Тип релиза
                    var normalizedType = NormalizeReleaseType(inner);
                    if (normalizedType != null)
                    {
                        if (string.IsNullOrEmpty(releaseType)) releaseType = normalizedType;
                        shouldRemove = true;
                    }

                    // Чисто технический блок тегов
                    if (IsPureTechBlock(inner))
                    {
                        shouldRemove = true;
                    }

                    // Завершающая релиз-группа в конце строки: [AniLibria]
                    if (!shouldRemove && c == '[' && string.IsNullOrEmpty(releaseGroup) && closeIndex >= text.TrimEnd().Length - 1)
                    {
                        if (!inner.Contains(' ') && inner.Length < 25 && !int.TryParse(inner, out _))
                        {
                            releaseGroup = inner;
                            shouldRemove = true;
                        }
                    }

                    if (shouldRemove)
                    {
                        i = closeIndex + 1;
                        continue;
                    }
                }
            }

            result.Append(c);
            i++;
        }

        return result.ToString().Trim();
    }

    private static string? ExtractResolutionFromInner(string inner)
    {
        var tokens = inner.Split([' ', ',', ';', '+', '/', '-'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var res = NormalizeResolution(token);
            if (res != null) return res;
        }
        return null;
    }
}
