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

    public async Task<List<TorrentEntity>> SearchTorrentsAsync(string query)
    {
        try
        {
            Log.Information("Torrents: Fetching RSS search for: {Query}", query);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var doc = await _nyaaClient.FetchSearchAsync(query, cts.Token);
            if (doc is null) return [];

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
                    var matchedAnime = activeAnime.FirstOrDefault(x =>
                        string.Equals(x.Title, torrent.AnimeTitle, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.EnglishTitle, torrent.AnimeTitle, StringComparison.OrdinalIgnoreCase));

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
}
