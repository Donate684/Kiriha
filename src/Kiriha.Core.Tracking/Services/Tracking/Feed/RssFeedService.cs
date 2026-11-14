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
                    .Where(x => x.Status == UserAnimeStatus.Watching || x.Status == UserAnimeStatus.PlanToWatch)
                    .ToList());

            foreach (var item in items)
            {
                var torrent = NyaaTorrentParser.ParseItem(item);
                if (torrent is null) continue;

                // Match only if this torrent contains an episode the user hasn't watched yet
                if (!string.IsNullOrEmpty(torrent.AnimeTitle))
                {
                    var parsedSeason = Kiriha.Utils.Parsing.AnimeParseCache.Parse(torrent.Title).Season;
                    var matchedAnime = MatchActiveAnime(activeAnime, torrent.AnimeTitle, parsedSeason);

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

    internal static AnimeEntity? MatchActiveAnime(List<AnimeEntity> activeAnime, string animeTitle, int? parsedSeason)
    {
        if (activeAnime.Count == 0 || string.IsNullOrWhiteSpace(animeTitle))
            return null;

        var normBase = NormalizeAlphaNum(animeTitle);
        if (string.IsNullOrEmpty(normBase))
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
            string[] seasonMarkers = [$"season{s}", $"{ordinal}season", $"s{s}", $"part{s}"];

            foreach (var candidate in activeAnime)
            {
                if (MatchesSequelCandidate(candidate.Title, normBase, seasonMarkers) ||
                    MatchesSequelCandidate(candidate.EnglishTitle, normBase, seasonMarkers) ||
                    candidate.AlternativeTitles.Any(alt => MatchesSequelCandidate(alt, normBase, seasonMarkers)))
                {
                    return candidate;
                }
            }

            return null;
        }

        // Exact case-insensitive match first
        var exact = activeAnime.FirstOrDefault(x =>
            string.Equals(x.Title, animeTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.EnglishTitle, animeTitle, StringComparison.OrdinalIgnoreCase) ||
            x.AlternativeTitles.Any(alt => string.Equals(alt, animeTitle, StringComparison.OrdinalIgnoreCase)));
        if (exact != null)
            return exact;

        // Normalized punctuation-insensitive match (e.g. "Re Zero..." == "Re:Zero...")
        return activeAnime.FirstOrDefault(x =>
            NormalizeAlphaNum(x.Title) == normBase ||
            NormalizeAlphaNum(x.EnglishTitle) == normBase ||
            x.AlternativeTitles.Any(alt => NormalizeAlphaNum(alt) == normBase));
    }

    private static bool MatchesSequelCandidate(string? candidateTitle, string normBase, string[] seasonMarkers)
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

        return false;
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
