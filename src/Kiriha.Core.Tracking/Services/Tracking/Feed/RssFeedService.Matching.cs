using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Repositories;
using Kiriha.Core.Services;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Integration;
using Kiriha.Core.Abstractions.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Feed;

public partial class RssFeedService
{
    /// <summary>
    /// Probe Nyaa search for a real-world airing signal. Returns the highest
    /// single-episode number found across trusted single-release torrents that
    /// map back to <paramref name="anime"/>, or <c>null</c> when nothing usable
    /// is found / the anime is not currently waiting for an episode.
    ///
    /// Caller (typically <c>AiringInfoService</c>) decides whether to trust the
    /// value — e.g. capping it by schedule+1 to reject hallucinated/junk hits.
    /// This method intentionally does not mutate the AnimeEntity or fire
    /// notifications, so all airing-state writes flow through one path.
    /// </summary>
    public async Task<int?> SyncEpisodesFromNyaaAsync(AnimeEntity anime, CancellationToken ct = default)
    {
        if (anime == null) return null;
        if (!NyaaTorrentParser.NeedsNyaaCheck(anime))
        {
            Log.Debug("RssFeedService: Skipping Nyaa probe for {Title} - next episode not due yet", anime.Title);
            return null;
        }

        try
        {
            Log.Debug("RssFeedService: Syncing {Title} from Nyaa.si search...", anime.Title);
            var doc = await _nyaaClient.FetchSearchAsync(anime.Title, ct);
            if (doc == null) return null;

            var items = doc.Descendants("item").Take(20).ToList(); // Top 20 results are enough

            int maxFound = 0;
            foreach (var item in items)
            {
                string? title = item.Element("title")?.Value;
                if (string.IsNullOrEmpty(title)) continue;

                int? epNum = NyaaTorrentParser.ExtractSingleEpisodeNumber(title);
                if (epNum == null) continue; // batch / range / multi-ep — skip

                var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
                var animeTitle = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
                if (string.IsNullOrEmpty(animeTitle)) continue;

                int? malId = await _mappingService.GetIdFromTitleAsync(animeTitle, new[] { anime });
                if (malId != anime.Id) continue;

                if (epNum.Value > maxFound) maxFound = epNum.Value;
            }

            return maxFound > 0 ? maxFound : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RssFeedService: Nyaa sync failed for {Title}", anime.Title);
            return null;
        }
    }

    public async Task<List<TorrentEntity>> SearchTorrentsAsync(string query)
    {
        try
        {
            Log.Information("Torrents: Fetching RSS search for: {Query}", query);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var doc = await _nyaaClient.FetchSearchAsync(query, cts.Token);
            if (doc == null) return new List<TorrentEntity>();

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
                if (torrent == null) continue;

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
            return new List<TorrentEntity>();
        }
    }
}
