using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kiriha.Core.Repositories;
using Kiriha.Core.Services;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Integration;
using Kiriha.Models.Entities;
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
            if (doc == null) return;

            var items = doc.Descendants("item").ToList();

            // Get only ongoing/watching items to save resources.
            // Snapshot on UI thread — ObservableCollection is not thread-safe.
            var activeAnime = await _uiDispatcher.InvokeAsync(() =>
                _animeRepo.GetCollection()
                    .Where(x => x.Status == UserAnimeStatus.Watching || x.Status == UserAnimeStatus.PlanToWatch)
                    .ToList());

            if (!activeAnime.Any()) return;

            var newTorrents = new List<TorrentEntity>();

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

                TorrentEntity torrent;
                if (existing != null)
                {
                    torrent = existing;
                }
                else
                {
                    torrent = new TorrentEntity
                    {
                        Title = title,
                        AnimeTitle = animeTitle ?? string.Empty,
                        Episode = episodeStr ?? string.Empty,
                        Resolution = resolution ?? string.Empty,
                        ReleaseGroup = group ?? string.Empty,
                        DownloadLink = item.Element("link")?.Value ?? string.Empty,
                        MagnetLink = !string.IsNullOrEmpty(infoHash) ? $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(title)}" : string.Empty,
                        PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.UtcNow,
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
