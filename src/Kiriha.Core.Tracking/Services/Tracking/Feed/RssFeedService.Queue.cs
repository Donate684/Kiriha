using System.Xml.Linq;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Feed;

public partial class RssFeedService
{
    public async Task CheckFeedsAsync()
    {
        // Gate: only hit Nyaa if at least one watching anime is actually awaiting a new episode
        // (NextEpisodeAt is null or already passed). Otherwise torrents have nothing new for us.
        // Snapshot on UI thread — ObservableCollection is not thread-safe.
        var awaitingEpisode = await _uiDispatcher.InvokeAsync(() =>
            _animeRepo.GetCollection().Any(NyaaTorrentParser.NeedsNyaaCheck));
        if (!awaitingEpisode)
        {
            Log.Debug("RssFeedService: Skipping Nyaa RSS check - no anime is awaiting a new episode");
            return;
        }

        Log.Debug("RssFeedService: Checking Nyaa.si RSS feed...");

        try
        {
            var doc = await _nyaaClient.FetchGlobalFeedAsync(CancellationToken.None);
            if (doc is null) return;

            var items = doc.Descendants("item").ToList();

            // Get only ongoing/watching items to save resources.
            // Snapshot on UI thread — ObservableCollection is not thread-safe.
            var activeAnime = await _uiDispatcher.InvokeAsync(() =>
                _animeRepo.GetCollection()
                    .Where(x => x.Status == UserAnimeStatus.Watching || x.Status == UserAnimeStatus.PlanToWatch)
                    .ToList());

            if (activeAnime.Count == 0) return;

            var newTorrents = new List<TorrentEntity>();

            foreach (var item in items)
            {
                string? title = item.Element("title")?.Value;
                if (string.IsNullOrEmpty(title)) continue;

                // Check if already in collection
                var existing = TorrentItems.FirstOrDefault(x => x.Title == title);
                if (existing != null && existing.IsMatched) continue;

                var torrent = existing ?? NyaaTorrentParser.ParseItem(item);
                if (torrent is null) continue;
                if (existing is null) torrent.IsNew = true;

                // Match with user list
                string matchTitle = !string.IsNullOrEmpty(torrent.AnimeTitle) ? torrent.AnimeTitle : torrent.Title;
                int? malId = await _mappingService.GetIdFromTitleAsync(matchTitle, activeAnime);

                if (malId != null)
                {
                    torrent.IsMatched = true;
                }

                if (existing is null)
                {
                    newTorrents.Add(torrent);
                }
            }

            if (newTorrents.Count > 0)
            {
                _uiDispatcher.Post(() =>
                {
                    // Add to the beginning of collection
                    foreach (var t in newTorrents.OrderBy(x => x.PublishDate))
                    {
                        if (!TorrentItems.Any(existing => existing.Title == t.Title))
                        {
                            TorrentItems.Insert(0, t);
                        }
                    }

                    // Trim collection
                    while (TorrentItems.Count > 100) TorrentItems.RemoveAt(TorrentItems.Count - 1);
                });
            }

            Log.Debug("RssFeedService: RSS check completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RssFeedService: Error during feed check");
        }
    }
}
