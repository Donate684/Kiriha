using System.Text.RegularExpressions;

namespace Kiriha.Core.Domain.Utils.Parsing;

public static partial class AnimeReleaseParser
{
    private static string? NormalizeResolution(ReadOnlySpan<char> span)
    {
        if (span.Equals("2160p", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("4k", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("uhd", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("3840x2160", StringComparison.OrdinalIgnoreCase))
            return "2160p";

        if (span.Equals("1440p", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("2560x1440", StringComparison.OrdinalIgnoreCase))
            return "1440p";

        if (span.Equals("1080p", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("1080i", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("1920x1080", StringComparison.OrdinalIgnoreCase))
            return "1080p";

        if (span.Equals("720p", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("1280x720", StringComparison.OrdinalIgnoreCase))
            return "720p";

        if (span.Equals("576p", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("720x576", StringComparison.OrdinalIgnoreCase))
            return "576p";

        if (span.Equals("480p", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("480i", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("848x480", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("720x480", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("640x480", StringComparison.OrdinalIgnoreCase))
            return "480p";

        if (span.Equals("360p", StringComparison.OrdinalIgnoreCase))
            return "360p";

        return null;
    }

    private static string? NormalizeReleaseType(ReadOnlySpan<char> span)
    {
        if (span.Equals("ova", StringComparison.OrdinalIgnoreCase) || span.Equals("oav", StringComparison.OrdinalIgnoreCase))
            return "OVA";
        if (span.Equals("ona", StringComparison.OrdinalIgnoreCase))
            return "ONA";
        if (span.Equals("movie", StringComparison.OrdinalIgnoreCase) || span.Equals("film", StringComparison.OrdinalIgnoreCase))
            return "Movie";
        if (span.Equals("special", StringComparison.OrdinalIgnoreCase) || span.Equals("specials", StringComparison.OrdinalIgnoreCase))
            return "Special";
        if (span.Equals("oad", StringComparison.OrdinalIgnoreCase))
            return "OAD";
        if (span.Equals("sp", StringComparison.OrdinalIgnoreCase))
            return "SP";
        return null;
    }

    private static bool IsBatchToken(ReadOnlySpan<char> span)
    {
        return span.Equals("batch", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("complete", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("complete series", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("pack", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPureTechBlock(string text)
    {
        var trimmed = text.Trim();
        if (TechLookup.Contains(trimmed.AsSpan())) return true;
        if (NormalizeResolution(trimmed.AsSpan()) is not null) return true;
        if (NormalizeReleaseType(trimmed.AsSpan()) is not null) return true;
        if (IsBatchToken(trimmed.AsSpan())) return true;

        var tokens = text.Split([' ', '-', '_', '/', ',', '+'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        foreach (var token in tokens)
        {
            var span = token.AsSpan();
            if (TechLookup.Contains(span)) continue;
            if (NormalizeResolution(span) is not null) continue;
            if (NormalizeReleaseType(span) is not null) continue;
            if (IsBatchToken(span)) continue;
            if (IsLanguageSuffix(span)) continue;
            if (int.TryParse(token, out int val) && (val is 8 or 10 or 264 or 265 or 720 or 1080 or 2160 or 480 or 576)) continue;
            return false;
        }
        return true;
    }

    private static bool IsTechToken(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty) return true;
        if (TechLookup.Contains(span)) return true;
        if (NormalizeResolution(span) is not null) return true;
        if (NormalizeReleaseType(span) is not null) return true;
        if (IsBatchToken(span)) return true;

        if (span.StartsWith("ddp", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("aac", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("ac3", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("eac3", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("flac", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("opus", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("dts", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("truehd", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("atmos", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("web-", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("webdl", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("webrip", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("web-dl", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("web-rip", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("h26", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("x26", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("h_26", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("x_26", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("10bit", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("8bit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string ProtectDottedTerms(string input)
    {
        return DottedCodecRegex().Replace(input, static m => m.Value.Replace('.', '_'));
    }

    private static string CleanTitleWithoutEpisode(string title, ref string resolution, ref string releaseType)
    {
        var tokens = title.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        var cleanTokens = new List<string>();

        foreach (var token in tokens)
        {
            var span = token.AsSpan().Trim("[](),.-_~");
            if (span.IsEmpty) continue;

            if (string.IsNullOrEmpty(resolution))
            {
                var res = NormalizeResolution(span);
                if (res != null)
                {
                    resolution = res;
                    continue;
                }
            }

            if (string.IsNullOrEmpty(releaseType))
            {
                var type = NormalizeReleaseType(span);
                if (type != null)
                {
                    releaseType = type;
                    continue;
                }
            }

            if (IsTechToken(span))
            {
                continue;
            }

            cleanTokens.Add(token);
        }

        return string.Join(" ", cleanTokens);
    }

    private static string ResolveBilingualTitle(string title)
    {
        int slashIndex = title.IndexOf(" / ", StringComparison.Ordinal);
        if (slashIndex < 0) return title;

        var part1 = title[..slashIndex].Trim();
        var part2 = title[(slashIndex + 3)..].Trim();

        bool p1HasCyrillic = HasCyrillic(part1);
        bool p1HasLatin = HasLatin(part1);
        bool p2HasCyrillic = HasCyrillic(part2);
        bool p2HasLatin = HasLatin(part2);

        if (p1HasCyrillic && !p1HasLatin && p2HasLatin)
        {
            return part2;
        }
        if (p2HasCyrillic && !p2HasLatin && p1HasLatin)
        {
            return part1;
        }

        return title;
    }

    private static bool HasCyrillic(ReadOnlySpan<char> span)
    {
        foreach (char c in span)
        {
            if (c is >= '\u0400' and <= '\u04FF' or >= '\u0500' and <= '\u052F')
                return true;
        }
        return false;
    }

    private static bool HasLatin(ReadOnlySpan<char> span)
    {
        foreach (char c in span)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
                return true;
        }
        return false;
    }

    private static bool IsLikelySceneReleasePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return false;

        // В точечном Scene-релизе минимум 2 точки и нет пробелов рядом с дефисом группы
        if (CountOccurrences(prefix, '.') >= 2)
            return true;

        // Или последний токен перед -Group является техническим тегом / разрешением
        int lastSep = prefix.LastIndexOfAny([' ', '.']);
        ReadOnlySpan<char> lastToken = lastSep >= 0 ? prefix.AsSpan(lastSep + 1) : prefix.AsSpan();
        lastToken = lastToken.Trim("[](),._");

        return !lastToken.IsEmpty && (IsTechToken(lastToken) || NormalizeResolution(lastToken) is not null);
    }

    private static bool IsValidIsolatedEpisode(string text, Match candidate)
    {
        string epCandidate = candidate.Groups["ep"].Value;
        if (!double.TryParse(epCandidate, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out double epNum) || epNum < 0 || epNum >= 1960)
            return false;

        var beforeSpan = text.AsSpan(0, candidate.Index).TrimEnd();

        // Защита от "Mob Psycho 100", "Area 88"
        if (beforeSpan.EndsWith("Mob Psycho", StringComparison.OrdinalIgnoreCase) && epCandidate == "100")
            return false;
        if (beforeSpan.EndsWith("Area", StringComparison.OrdinalIgnoreCase) && epCandidate == "88")
            return false;

        // Защита от "Kaiju No. 8", "No. 6"
        if (beforeSpan.EndsWith("No.", StringComparison.OrdinalIgnoreCase) ||
            beforeSpan.EndsWith("No", StringComparison.OrdinalIgnoreCase) ||
            beforeSpan.EndsWith("#", StringComparison.Ordinal) ||
            beforeSpan.EndsWith("\u2116", StringComparison.Ordinal))
        {
            return false;
        }

        // Если число записано с ведущим нулем ("00", "01", "08", "05.5"), это явно номер серии
        if (epCandidate.StartsWith('0'))
            return true;

        // Если непосредственно перед числом стоит тип релиза (например, "KonoSuba OVA 1")
        int prevSep = beforeSpan.LastIndexOfAny(' ', '.');
        var prevToken = (prevSep >= 0 ? beforeSpan[(prevSep + 1)..] : beforeSpan).Trim("[](),._");
        if (NormalizeReleaseType(prevToken) is not null)
            return true;

        // Если после числа идут технические теги/разрешение (например, "Bleach 366 1080p")
        var afterSpan = text.AsSpan(candidate.Index + candidate.Length).Trim();
        if (!afterSpan.IsEmpty)
        {
            int nextSep = afterSpan.IndexOfAny(' ', '.');
            var nextToken = (nextSep >= 0 ? afterSpan[..nextSep] : afterSpan).Trim("[](),._");
            if (IsTechToken(nextToken) || NormalizeResolution(nextToken) is not null)
                return true;
        }

        return false;
    }

    private static string TrimDelimiters(string s)
    {
        return s.Trim(' ', '-', '_', ':', '~', '.');
    }

    private static int CountOccurrences(string s, char target)
    {
        int count = 0;
        foreach (char c in s)
        {
            if (c == target) count++;
        }
        return count;
    }
}
