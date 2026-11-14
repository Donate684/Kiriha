using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Feed;

public class RssFeedService : IRssFeedService
{
    private readonly NyaaFeedClient _nyaaClient;
    private readonly IAnimeRepository _animeRepo;
    private readonly IUiDispatcher _uiDispatcher;

    public RssFeedService(
        NyaaFeedClient nyaaClient,
        IAnimeRepository animeRepo,
        IUiDispatcher uiDispatcher)
    {
        _nyaaClient = nyaaClient;
        _animeRepo = animeRepo;
        _uiDispatcher = uiDispatcher;
    }

    public async Task<List<TorrentEntity>> SearchTorrentsAsync(string query, CancellationToken ct = default)
    {
        try
        {
            Log.Information("Torrents: Fetching RSS search for: {Query}", query);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var doc = await _nyaaClient.FetchSearchAsync(query, linkedCts.Token);
            if (doc is null || linkedCts.Token.IsCancellationRequested) return [];

            Log.Information("Torrents: Parsing XML response...");
            var items = doc.Descendants("item").ToList();
            Log.Information("Torrents: Found {Count} items in XML", items.Count);
            var results = new List<TorrentEntity>();

            // Snapshot ObservableCollection on UI thread to avoid "Collection was modified" races.
            var activeAnime = await _uiDispatcher.InvokeAsync(() =>
                _animeRepo.GetCollection()
                    .Where(x => x.MediaKind == MediaKind.Anime &&
                                (x.Status == UserAnimeStatus.Watching || x.Status == UserAnimeStatus.PlanToWatch))
                    .ToList());

            foreach (var item in items)
            {
                var torrent = NyaaTorrentParser.ParseItem(item);
                if (torrent is null) continue;

                // Match only if this torrent contains an episode the user hasn't watched yet
                if (!string.IsNullOrEmpty(torrent.AnimeTitle))
                {
                    var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(torrent.Title);
                    var baseTitle = !string.IsNullOrWhiteSpace(parsed.Title) ? parsed.Title : torrent.AnimeTitle;
                    var matchedAnime = MatchActiveAnime(activeAnime, baseTitle, parsed.Season, parsed.Year);

                    if (matchedAnime != null
                        && int.TryParse(torrent.Episode, out var epNum)
                        && epNum > matchedAnime.Progress)
                    {
                        torrent.IsMatched = true;
                    }
                }

                results.Add(torrent);
            }
            return results;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RssFeedService: Search failed for {Query}", query);
            return [];
        }
    }

    internal static AnimeEntity? MatchActiveAnime(
        List<AnimeEntity> activeAnime,
        string animeTitle,
        int? parsedSeason,
        int? parsedYear = null)
    {
        if (activeAnime.Count == 0 || string.IsNullOrWhiteSpace(animeTitle))
            return null;

        var parsedInput = Kiriha.Utils.Parsing.AnimeParseCache.Parse(animeTitle);
        int? effectiveYear = parsedYear ?? parsedInput.Year;
        var cleanBaseTitle = effectiveYear.HasValue && !string.IsNullOrWhiteSpace(parsedInput.Title)
            ? parsedInput.Title
            : animeTitle;

        var normBase = NormalizeAlphaNum(cleanBaseTitle);
        if (string.IsNullOrEmpty(normBase))
            return null;

        var eligible = activeAnime.Where(x => x.MediaKind == MediaKind.Anime && IsYearCompatible(x, effectiveYear)).ToList();
        if (eligible.Count == 0)
            return null;

        if (parsedSeason is > 1)
        {
            int s = parsedSeason.Value;
            string ordinal = s switch
            {
                2 => "2nd",
                3 => "3rd",
                _ => $"{s}th"
            };
            string? romanNumeral = s switch
            {
                2 => "ii",
                3 => "iii",
                4 => "iv",
                5 => "v",
                6 => "vi",
                _ => null
            };
            string[] seasonMarkers = [$"season{s}", $"{ordinal}season", $"s{s}", $"part{s}"];

            foreach (var candidate in eligible)
            {
                if (MatchesSequelCandidate(candidate.Title, normBase, seasonMarkers, s, romanNumeral) ||
                    MatchesSequelCandidate(candidate.EnglishTitle, normBase, seasonMarkers, s, romanNumeral) ||
                    candidate.AlternativeTitles.Any(alt => MatchesSequelCandidate(alt, normBase, seasonMarkers, s, romanNumeral)))
                {
                    return candidate;
                }
            }

            return null;
        }

        if (effectiveYear.HasValue)
        {
            string yearQualified = $"{cleanBaseTitle} ({effectiveYear.Value})";
            string normWithYear = NormalizeAlphaNum(yearQualified);

            var yearMatch = eligible.FirstOrDefault(x =>
                string.Equals(x.Title, yearQualified, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.EnglishTitle, yearQualified, StringComparison.OrdinalIgnoreCase) ||
                NormalizeAlphaNum(x.Title) == normWithYear ||
                NormalizeAlphaNum(x.EnglishTitle) == normWithYear ||
                x.AlternativeTitles.Any(alt => NormalizeAlphaNum(alt) == normWithYear));
            if (yearMatch != null)
                return yearMatch;

            // Prefer candidate that explicitly matches effectiveYear
            var explicitYearMatch = eligible.FirstOrDefault(x =>
                (x.StartYear ?? x.AiringDate?.Year) is int y &&
                Math.Abs(y - effectiveYear.Value) <= 1 &&
                (NormalizeAlphaNum(x.Title) == normBase ||
                 NormalizeAlphaNum(x.EnglishTitle) == normBase ||
                 x.AlternativeTitles.Any(alt => NormalizeAlphaNum(alt) == normBase)));
            if (explicitYearMatch != null)
                return explicitYearMatch;

            // Fallback to shallow Watching/PTW entries whose StartYear has not been hydrated yet
            return eligible.FirstOrDefault(x =>
                (x.StartYear ?? x.AiringDate?.Year) is null &&
                (NormalizeAlphaNum(x.Title) == normBase ||
                 NormalizeAlphaNum(x.EnglishTitle) == normBase ||
                 x.AlternativeTitles.Any(alt => NormalizeAlphaNum(alt) == normBase)));
        }

        // Exact case-insensitive match first
        var exact = eligible.FirstOrDefault(x =>
            string.Equals(x.Title, cleanBaseTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.EnglishTitle, cleanBaseTitle, StringComparison.OrdinalIgnoreCase) ||
            x.AlternativeTitles.Any(alt => string.Equals(alt, cleanBaseTitle, StringComparison.OrdinalIgnoreCase)));
        if (exact != null)
            return exact;

        // Normalized punctuation-insensitive match (e.g. "Re Zero..." == "Re:Zero...")
        return eligible.FirstOrDefault(x =>
            NormalizeAlphaNum(x.Title) == normBase ||
            NormalizeAlphaNum(x.EnglishTitle) == normBase ||
            x.AlternativeTitles.Any(alt => NormalizeAlphaNum(alt) == normBase));
    }

    private static bool IsYearCompatible(AnimeEntity candidate, int? parsedYear)
    {
        if (!parsedYear.HasValue)
            return true;

        int? cy = candidate.StartYear ?? candidate.AiringDate?.Year;
        return !cy.HasValue || Math.Abs(cy.Value - parsedYear.Value) <= 1;
    }

    private static bool MatchesSequelCandidate(
        string? candidateTitle,
        string normBase,
        string[] seasonMarkers,
        int seasonNumber,
        string? romanNumeral)
    {
        var normCandidate = NormalizeAlphaNum(candidateTitle);
        if (string.IsNullOrEmpty(normCandidate) || !normCandidate.StartsWith(normBase, StringComparison.Ordinal))
            return false;

        var tail = normCandidate[normBase.Length..];
        foreach (var marker in seasonMarkers)
        {
            if (tail.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(candidateTitle))
        {
            var tokens = TokenizeAlphaNumWords(candidateTitle);
            int consumedChars = 0;
            int idx = 0;
            while (idx < tokens.Count && consumedChars < normBase.Length)
            {
                consumedChars += tokens[idx].Length;
                idx++;
            }

            if (consumedChars == normBase.Length)
            {
                string numStr = seasonNumber.ToString();
                for (int i = idx; i < tokens.Count; i++)
                {
                    if (tokens[i] == numStr || (romanNumeral != null && tokens[i] == romanNumeral))
                        return true;
                }
            }
        }

        return false;
    }

    private static List<string> TokenizeAlphaNumWords(string value)
    {
        var words = new List<string>();
        int start = -1;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                words.Add(value[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        if (start >= 0)
            words.Add(value[start..].ToLowerInvariant());

        return words;
    }

    private static string NormalizeAlphaNum(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Span<char> buf = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        int n = 0;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                buf[n++] = char.ToLowerInvariant(ch);
        }

        return new string(buf[..n]);
    }
}
