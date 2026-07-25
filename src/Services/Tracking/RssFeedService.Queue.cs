using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.Services.Tracking;

public partial class RssFeedService
{
    public async Task CheckFeedsAsync()
    {
        // Gate: only hit Nyaa if at least one watching anime is actually awaiting a new episode
        // (NextEpisodeAt is null or already passed). Otherwise torrents have nothing new for us.
        // Snapshot on UI thread — ObservableCollection is not thread-safe.
        var awaitingEpisode = await _uiDispatcher.InvokeAsync(() =>
            _animeRepo.Collection.Any(NyaaTorrentParser.NeedsNyaaCheck));
        if (!awaitingEpisode)
        {
            Log.Debug("RssFeedService: Skipping Nyaa RSS check - no anime is awaiting a new episode");
            return;
        }

        Log.Debug("RssFeedService: Checking Nyaa.si RSS feed...");

        try
        {
            var doc = await _nyaaClient.FetchGlobalFeedAsync(CancellationToken.None);
            if (doc == null) return;

            var items = doc.Descendants("item").ToList();

            // Get only ongoing/watching items to save resources.
            // Snapshot on UI thread — ObservableCollection is not thread-safe.
            var activeAnime = await _uiDispatcher.InvokeAsync(() =>
                _animeRepo.Collection
                    .Where(x => x.Status == UserAnimeStatus.Watching || x.Status == UserAnimeStatus.PlanToWatch)
                    .ToList());

            if (!activeAnime.Any()) return;

            var newTorrents = new List<Kiriha.Models.TorrentItem>();

            foreach (var item in items)
            {
                string? title = item.Element("title")?.Value;
                if (string.IsNullOrEmpty(title)) continue;

                // Check if already in collection
                var existing = TorrentItems.FirstOrDefault(x => x.Title == title);
                if (existing != null && existing.IsMatched) continue;

                // Parse title with Anitomy
                var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
                var animeTitle = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
                var episodeStr = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;
                var resolution = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementVideoResolution)?.Value;
                var group = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementReleaseGroup)?.Value;

                // Single-episode releases only — batches / ranges return null and
                // are surfaced as torrent rows but not used to bump EpisodesAired.
                var nyaaNs = XNamespace.Get("https://nyaa.si/xmlns/nyaa");
                var infoHash = item.Element(nyaaNs + "infoHash")?.Value;

                Kiriha.Models.TorrentItem torrent;
                if (existing != null)
                {
                    torrent = existing;
                }
                else
                {
                    torrent = new Kiriha.Models.TorrentItem
                    {
                        Title = title,
                        AnimeTitle = animeTitle,
                        Episode = episodeStr,
                        Resolution = resolution,
                        ReleaseGroup = group,
                        DownloadLink = item.Element("link")?.Value,
                        MagnetLink = !string.IsNullOrEmpty(infoHash) ? $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(title)}" : null,
                        PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.Now,
                        IsNew = true
                    };
                }

                // Match with user list
                string matchTitle = !string.IsNullOrEmpty(animeTitle) ? animeTitle : title;
                int? malId = await _mappingService.GetIdFromTitleAsync(matchTitle, activeAnime);

                if (malId != null)
                {
                    torrent.IsMatched = true;
                }

                if (existing == null)
                {
                    newTorrents.Add(torrent);
                }
            }

            if (newTorrents.Any())
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
