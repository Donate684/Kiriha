using System.Text.RegularExpressions;

namespace Kiriha.Core.Domain.Utils.Parsing;

/// <summary>
/// Высокопроизводительный детерминированный парсер имен релизов, файлов и заголовков окон аниме.
/// Поддерживает форматы Fansub / Nyaa, Scene / P2P (WEB-DL), русскоязычные релизы (AniLibria)
/// и заголовки внешних плееров.
/// </summary>
public static partial class AnimeReleaseParser
{
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

            int qIdx = text.IndexOf('?');
            if (qIdx >= 0) text = text[..qIdx];
            int hashIdx = text.IndexOf('#');
            if (hashIdx >= 0) text = text[..hashIdx];
        }
        else if (text.StartsWith('/') && !text.Contains(" / "))
        {
            int lastSlash = text.LastIndexOf('/');
            if (lastSlash >= 0)
                text = text[(lastSlash + 1)..];

            int qIdx = text.IndexOf('?');
            if (qIdx >= 0) text = text[..qIdx];
            int hashIdx = text.IndexOf('#');
            if (hashIdx >= 0) text = text[..hashIdx];
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
        int? year = null;
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
            ref year,
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

        // Поиск типа релиза в теле названия
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
        titlePart = MultipleSpacesRegex().Replace(titlePart, " ").Trim();
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
            Year = year,
            ReleaseGroup = releaseGroup,
            Resolution = resolution,
            ReleaseType = releaseType,
            EpisodeTitle = episodeTitle,
            IsBatch = isBatch
        };
    }
}
