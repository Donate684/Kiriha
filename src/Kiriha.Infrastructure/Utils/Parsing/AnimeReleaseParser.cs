using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Kiriha.Utils.Parsing;

/// <summary>
/// Высокопроизводительный детерминированный парсер имен релизов, файлов и заголовков окон аниме.
/// Поддерживает форматы Fansub / Nyaa, Scene / P2P (WEB-DL), русскоязычные релизы (AniLibria)
/// и заголовки внешних плееров.
/// </summary>
public static partial class AnimeReleaseParser
{
    private static readonly FrozenSet<string> Extensions = new string[]
    {
        // Video
        ".mkv", ".mp4", ".avi", ".webm", ".m2ts", ".ts", ".wmv", ".mov", ".flv",
        // Audio
        ".mka", ".flac", ".opus", ".aac", ".mp3", ".ogg", ".ac3", ".wav",
        // Subtitles
        ".ass", ".srt", ".ssa", ".vtt", ".sub", ".idx"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> TechKeywords = new string[]
    {
        // Quality / Source
        "4k", "uhd", "2160p", "1440p", "1080p", "1080i", "720p", "576p", "480p", "360p",
        "1920x1080", "1280x720", "3840x2160", "848x480", "720x480", "640x480",
        "web-dl", "webdl", "web-rip", "webrip", "web", "bd", "bluray", "blu-ray", "bdrip", "brrip",
        "dvd", "dvdrip", "hdtv", "tvrip", "raw", "vostfr",

        // Codecs / Formats
        "x264", "x265", "h264", "h265", "h.264", "h.265", "avc", "hevc", "av1",
        "10bit", "10-bit", "8bit", "8-bit", "hi10p", "hi10",
        "hdr", "hdr10", "hdr10+", "dv", "dovi", "sdr", "remux",

        // Audio
        "aac", "aac2.0", "aac5.1", "aacx2", "ac3", "eac3", "ddp", "ddp2.0", "ddp5.1", "dd+",
        "dts", "dts-hd", "truehd", "atmos", "flac", "flac2.0", "flac5.1", "flacx2", "opus", "mp3",
        "2.0", "5.1", "7.1", "2ch", "6ch", "8ch",

        // Providers
        "cr", "crunchyroll", "nf", "netflix", "amzn", "amazon", "hidive", "b-global", "bilibili",
        "adn", "dsnp", "abema", "funi", "funimation",

        // Subtitles & Audio info
        "multi-subs", "multisubs", "multi-sub", "multisub", "multiple subtitle", "multiple subtitles",
        "multi", "multiple", "subtitle", "subtitles", "sub", "subs", "dub", "dubbed",
        "dual-audio", "dual audio", "multi-audio", "multiaudio", "softsub", "softsubs", "hardsub", "hardsubs",

        // Editions & Versions
        "batch", "complete", "all", "pack", "uncensored", "censored", "repack", "proper", "v2", "v3", "v4"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> TechLookup =
        TechKeywords.GetAlternateLookup<ReadOnlySpan<char>>();

    [GeneratedRegex(@"\[[0-9A-Fa-f]{8}\]|\([0-9A-Fa-f]{8}\)", RegexOptions.CultureInvariant)]
    private static partial Regex Crc32Regex();

    [GeneratedRegex(@"\s+-\s+(?:mpv|VLC media player|MPC-HC|PotPlayer)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlayerWindowSuffixRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))[sS](?<season>\d{1,2})[eE](?<ep1>\d{1,4}(?:\.\d)?)(?:-?[eE]?(?<ep2>\d{1,4}))?(?:v\d+)?(?:\b|(?=[._\-\s]))", RegexOptions.CultureInvariant)]
    private static partial Regex SxxExxRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?<season>\d{1,2})x(?<ep>\d{2,4})(?:v\d+)?(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XFormatRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?:Ep|Eps|Episode|Episodes|\u0421\u0435\u0440\u0438\u044F|\u0421\u0435\u0440|\u0412\u044B\u043F\u0443\u0441\u043A)\.?\s*(?<ep>\d{1,4}(?:\.\d)?)(?:v\d+)?(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitEpRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?<ep>\d{1,4}(?:\.\d)?)\s*\u0441\u0435\u0440\u0438\u044F(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CyrillicEpRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?<ep>\d{1,4})\s*(?:\u0438\u0437|of)\s*\d{1,4}(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProgressEpRegex();

    [GeneratedRegex(@"(?:\s+-\s+|\s+~\s+)(?<ep1>\d{1,4}(?:\.\d)?)(?:v\d+)?(?:\s*[-~]\s*(?<ep2>\d{1,4}))?(?:\s|$|\b)", RegexOptions.CultureInvariant)]
    private static partial Regex HyphenEpRegex();

    [GeneratedRegex(@"\b(?:Season|Saison|Series)\s*(?<season>\d{1,2})\b|\b(?<season>\d{1,2})(?:st|nd|rd|th)\s+Season\b|\b(?<season>\d{1,2})\s*\u0441\u0435\u0437\u043E\u043D\b|\b(?:(?:\u0422\u0412|TV))-(?<season>\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitSeasonRegex();

    [GeneratedRegex(@"(?:\s+-\s+|\s+)S(?<season>\d{1,2})(?:\s+-\s+|\s+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneSeasonRegex();

    [GeneratedRegex(@"(?<=\s)(?<ep>\d{1,3}(?:\.\d)?)(?:v\d+)?(?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex IsolatedNumberRegex();

    [GeneratedRegex(@"-([A-Za-z0-9_]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex SceneGroupRegex();

    [GeneratedRegex(@"\b(?:[A-Za-z0-9]+)\.(?:[0-9]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DottedCodecRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?<type>OVA|ONA|OAD|SP|Special|Specials)(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleReleaseTypeRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultipleSpacesRegex();

    /// <summary>
    /// Разбирает переданное имя файла или заголовок окна в структуру <see cref="ParsedAnimeRelease"/>.
    /// </summary>
    public static ParsedAnimeRelease Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ParsedAnimeRelease.Empty;

        string text = input.Trim();

        // 1. Очистка пути к файлу или URL
        if (text.Contains('\\'))
        {
            text = Path.GetFileName(text);
        }
        else if (text.Contains("://"))
        {
            int lastSlash = text.LastIndexOf('/');
            if (lastSlash >= 0)
                text = text[(lastSlash + 1)..];
        }
        else if (text.StartsWith('/') && !text.Contains(" / "))
        {
            int lastSlash = text.LastIndexOf('/');
            if (lastSlash >= 0)
                text = text[(lastSlash + 1)..];
        }

        // Удаление суффикса плеера, если он есть
        text = PlayerWindowSuffixRegex().Replace(text, "");

        // 2. Удаление известных расширений файлов и языковых суффиксов
        int lastDot = text.LastIndexOf('.');
        if (lastDot > 0)
        {
            string ext = text[lastDot..];
            if (Extensions.Contains(ext))
            {
                text = text[..lastDot];

                int prevDot = text.LastIndexOf('.');
                if (prevDot > 0)
                {
                    var langSpan = text.AsSpan(prevDot + 1);
                    if (IsLanguageSuffix(langSpan))
                    {
                        text = text[..prevDot];
                    }
                }
            }
        }

        // 3. Удаление CRC32 хэшей
        text = Crc32Regex().Replace(text, "").Trim();

        string releaseGroup = string.Empty;
        string resolution = string.Empty;
        string releaseType = string.Empty;
        int? season = null;
        string episode = string.Empty;
        string episodeTitle = string.Empty;
        bool isBatch = false;

        // 4. Извлечение лидирующей релиз-группы [Group Name]
        var trimmed = text.Trim();
        if (trimmed.StartsWith('['))
        {
            int close = trimmed.IndexOf(']');
            if (close > 1)
            {
                var groupCandidate = trimmed[1..close].Trim();
                if (!IsPureTechBlock(groupCandidate) &&
                    NormalizeResolution(groupCandidate) is null &&
                    !int.TryParse(groupCandidate, out _) &&
                    !groupCandidate.StartsWith("\u0422\u0412-", StringComparison.OrdinalIgnoreCase) &&
                    !groupCandidate.StartsWith("TV-", StringComparison.OrdinalIgnoreCase))
                {
                    releaseGroup = groupCandidate;
                    text = trimmed[(close + 1)..].Trim();
                }
            }
        }

        // Извлечение Scene релиз-группы из хвоста (-GroupName)
        // Срабатывает только если перед -Group идет технический токен, разрешение или строка в точечном Scene-формате,
        // чтобы не отрезать части названий вроде "Psycho-Pass", "K-On", "Yu-Gi-Oh", "Re-Main".
        if (string.IsNullOrEmpty(releaseGroup))
        {
            var matchScene = SceneGroupRegex().Match(text);
            if (matchScene.Success && matchScene.Index > 0)
            {
                var groupCandidate = matchScene.Groups[1].Value;
                var prefixBeforeDash = text[..matchScene.Index];
                if (!int.TryParse(groupCandidate, out _) &&
                    !IsPureTechBlock(groupCandidate) &&
                    NormalizeResolution(groupCandidate) is null &&
                    IsLikelySceneReleasePrefix(prefixBeforeDash))
                {
                    releaseGroup = groupCandidate;
                    text = prefixBeforeDash.Trim();
                }
            }
        }

        // 5. Разбор скобочных блоков [...] и (...)
        text = ProcessBrackets(
            text,
            ref releaseGroup,
            ref resolution,
            ref releaseType,
            ref season,
            ref episode,
            ref isBatch);

        // 6. Поиск якоря сезона / эпизода в оставшемся теле строки
        string titlePart = text;
        string remainderPart = string.Empty;

        // Якорь 1: SxxExx
        var matchSxx = SxxExxRegex().Match(text);
        if (matchSxx.Success)
        {
            season ??= int.Parse(matchSxx.Groups["season"].Value);
            if (matchSxx.Groups["ep2"].Success)
            {
                episode = $"{matchSxx.Groups["ep1"].Value}-{matchSxx.Groups["ep2"].Value}";
                isBatch = true;
            }
            else
            {
                episode = matchSxx.Groups["ep1"].Value;
            }

            titlePart = text[..matchSxx.Index];
            remainderPart = text[(matchSxx.Index + matchSxx.Length)..];
        }
        else
        {
            // Якорь 1б: 1x05
            var matchX = XFormatRegex().Match(text);
            if (matchX.Success)
            {
                season ??= int.Parse(matchX.Groups["season"].Value);
                episode = matchX.Groups["ep"].Value;
                titlePart = text[..matchX.Index];
                remainderPart = text[(matchX.Index + matchX.Length)..];
            }
            else
            {
                // Якорь 2: Явные префиксы серии
                var matchEpPrefix = ExplicitEpRegex().Match(text);
                var matchCyrillicEp = CyrillicEpRegex().Match(text);
                var matchProgressEp = ProgressEpRegex().Match(text);

                Match? epMatch = null;
                if (matchEpPrefix.Success) epMatch = matchEpPrefix;
                else if (matchCyrillicEp.Success) epMatch = matchCyrillicEp;
                else if (matchProgressEp.Success) epMatch = matchProgressEp;

                if (epMatch != null)
                {
                    episode = epMatch.Groups["ep"].Value;
                    titlePart = text[..epMatch.Index];
                    remainderPart = text[(epMatch.Index + epMatch.Length)..];
                }
                else
                {
                    // Якорь 3: Разделитель тире перед номером серии
                    var hyphenMatches = HyphenEpRegex().Matches(text);
                    if (hyphenMatches.Count > 0)
                    {
                        var lastMatch = hyphenMatches[^1];
                        if (lastMatch.Groups["ep2"].Success)
                        {
                            episode = $"{lastMatch.Groups["ep1"].Value}-{lastMatch.Groups["ep2"].Value}";
                            isBatch = true;
                        }
                        else
                        {
                            episode = lastMatch.Groups["ep1"].Value;
                        }

                        titlePart = text[..lastMatch.Index];
                        remainderPart = text[(lastMatch.Index + lastMatch.Length)..];
                    }
                    else
                    {
                        // Якорь 4: Изолированное число перед техническими тегами (fallback)
                        var isolatedMatches = IsolatedNumberRegex().Matches(text);
                        if (isolatedMatches.Count > 0)
                        {
                            var candidate = isolatedMatches[^1];
                            if (IsValidIsolatedEpisode(text, candidate))
                            {
                                episode = candidate.Groups["ep"].Value;
                                titlePart = text[..candidate.Index];
                                remainderPart = text[(candidate.Index + candidate.Length)..];
                            }
                        }
                    }
                }
            }
        }

        // 7. Очистка названия тайтла (Title)
        // Если тайтл в точечном формате Scene, преобразуем точки в пробелы до поиска сезона и типа релиза
        if (titlePart.Contains('.'))
        {
            int spaces = CountOccurrences(titlePart, ' ');
            int dots = CountOccurrences(titlePart, '.');
            if (spaces <= 1 && dots >= 2)
            {
                titlePart = titlePart.Replace('.', ' ').Replace('_', ' ');
            }
        }

        // Поиск явных маркеров сезона в названии, если сезон еще не определен
        if (season is null)
        {
            var matchExplicitSeason = ExplicitSeasonRegex().Match(titlePart);
            if (matchExplicitSeason.Success)
            {
                season = int.Parse(matchExplicitSeason.Groups["season"].Value);
                titlePart = titlePart.Remove(matchExplicitSeason.Index, matchExplicitSeason.Length);
            }
            else
            {
                var matchStandaloneSeason = StandaloneSeasonRegex().Match(titlePart);
                if (matchStandaloneSeason.Success)
                {
                    season = int.Parse(matchStandaloneSeason.Groups["season"].Value);
                    titlePart = titlePart.Remove(matchStandaloneSeason.Index, matchStandaloneSeason.Length);
                }
            }
        }

        // Поиск типа релиза в теле названия (например "KonoSuba OVA 1" -> Title: "KonoSuba", ReleaseType: "OVA")
        if (string.IsNullOrEmpty(releaseType))
        {
            var matchRelType = TitleReleaseTypeRegex().Match(titlePart);
            if (matchRelType.Success)
            {
                var normType = NormalizeReleaseType(matchRelType.Groups["type"].ValueSpan);
                if (normType != null)
                {
                    releaseType = normType;
                    titlePart = titlePart.Remove(matchRelType.Index, matchRelType.Length);
                }
            }
        }

        titlePart = TrimDelimiters(titlePart);

        // Нормализация множественных пробелов
        titlePart = MultipleSpacesRegex().Replace(titlePart, " ").Trim();

        // Разрешение русско-английского слэша AniLibria
        titlePart = ResolveBilingualTitle(titlePart);
        titlePart = TrimDelimiters(titlePart);

        // 8. Разбор остаточного хвоста (EpisodeTitle, Resolution, ReleaseType, Batch)
        if (!string.IsNullOrEmpty(remainderPart))
        {
            remainderPart = ProtectDottedTerms(remainderPart);
            char[] splitChars = [' ', '.'];
            var tokens = remainderPart.Split(splitChars, StringSplitOptions.RemoveEmptyEntries);

            var episodeTitleWords = new List<string>();

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

                if (IsBatchToken(span))
                {
                    isBatch = true;
                    continue;
                }

                if (IsTechToken(span))
                {
                    continue;
                }

                // Числовые токены в хвосте технического описания Scene релиза (каналы, битрейты) пропускаются
                if (int.TryParse(token, out _))
                {
                    continue;
                }

                episodeTitleWords.Add(span.ToString());
            }

            if (episodeTitleWords.Count > 0)
            {
                episodeTitle = string.Join(" ", episodeTitleWords);
                episodeTitle = TrimDelimiters(episodeTitle);
            }
        }

        // Если номер серии не найден вообще, очищаем весь текст от тех. тегов и года
        if (string.IsNullOrEmpty(episode))
        {
            titlePart = CleanTitleWithoutEpisode(titlePart, ref resolution, ref releaseType);
        }

        titlePart = MultipleSpacesRegex().Replace(titlePart, " ").Trim();

        return new ParsedAnimeRelease
        {
            Title = titlePart,
            Episode = episode,
            Season = season,
            ReleaseGroup = releaseGroup,
            Resolution = resolution,
            ReleaseType = releaseType,
            EpisodeTitle = episodeTitle,
            IsBatch = isBatch
        };
    }

    private static string ProcessBrackets(
        string text,
        ref string releaseGroup,
        ref string resolution,
        ref string releaseType,
        ref int? season,
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
                        if (int.TryParse(leftPart, out _) && int.TryParse(rightPart, out _))
                        {
                            episode = $"{leftPart}-{rightPart}";
                            isBatch = true;
                            shouldRemove = true;
                        }
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
               span.Equals("all", StringComparison.OrdinalIgnoreCase) ||
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
            span.StartsWith("web", StringComparison.OrdinalIgnoreCase) ||
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

    private static bool IsLanguageSuffix(ReadOnlySpan<char> span)
    {
        return span.Equals("en", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("ru", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("ja", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("eng", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("rus", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("jpn", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("ger", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("fre", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("fra", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("spa", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("ita", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("chi", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("kor", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("chs", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("cht", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("default", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("forced", StringComparison.OrdinalIgnoreCase);
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
        if (!int.TryParse(epCandidate, out int epNum) || epNum <= 0 || epNum >= 1960)
            return false;

        // Защита от "Kaiju No. 8", "No. 6"
        var beforeSpan = text.AsSpan(0, candidate.Index).TrimEnd();
        if (beforeSpan.EndsWith("No.", StringComparison.OrdinalIgnoreCase) ||
            beforeSpan.EndsWith("No", StringComparison.OrdinalIgnoreCase) ||
            beforeSpan.EndsWith("#", StringComparison.Ordinal) ||
            beforeSpan.EndsWith("\u2116", StringComparison.Ordinal))
        {
            return false;
        }

        // Если число записано с ведущим нулем ("01", "08"), это явно номер серии
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
