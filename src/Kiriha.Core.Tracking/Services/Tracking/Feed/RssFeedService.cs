using Kiriha.Core.Domain.Utils.Parsing;
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
                    var parsed = Kiriha.Core.Domain.Utils.Parsing.AnimeParseCache.Parse(torrent.Title);
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
        IReadOnlyList<AnimeEntity> activeAnime,
        string animeTitle,
        int? parsedSeason,
        int? parsedYear = null) =>
        Kiriha.Core.Domain.Utils.Parsing.AnimeLibraryMatcher.MatchActiveAnime(
            activeAnime,
            animeTitle,
            parsedSeason,
            parsedYear);
}
