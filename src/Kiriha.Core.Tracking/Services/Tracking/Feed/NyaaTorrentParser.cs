using System.Xml.Linq;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Feed;

internal static class NyaaTorrentParser
{
    public static TorrentEntity? ParseItem(XElement item)
    {
        string? title = item.Element("title")?.Value;
        if (string.IsNullOrEmpty(title)) return null;

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
        var animeTitle = parsed.Title;
        var episodeStr = parsed.Episode;
        var resolution = parsed.Resolution;
        var group = parsed.ReleaseGroup;

        var nyaaNs = XNamespace.Get(Kiriha.Core.Domain.Constants.AppConstants.Api.Nyaa.XmlNamespace);
        var infoHash = item.Element(nyaaNs + "infoHash")?.Value;
        var sizeStr = item.Element(nyaaNs + "size")?.Value ?? string.Empty;
        var seedersStr = item.Element(nyaaNs + "seeders")?.Value;
        var leechersStr = item.Element(nyaaNs + "leechers")?.Value;
        var downloadsStr = item.Element(nyaaNs + "downloads")?.Value;
        var categoryStr = item.Element(nyaaNs + "category")?.Value ?? string.Empty;

        int.TryParse(seedersStr, out var seeders);
        int.TryParse(leechersStr, out var leechers);
        int.TryParse(downloadsStr, out var downloads);

        bool isBatch = parsed.IsBatch
            || title.Contains("Batch", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Season", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Complete", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrEmpty(episodeStr)
                && !title.Contains("Movie", StringComparison.OrdinalIgnoreCase)
                && !title.Contains("Film", StringComparison.OrdinalIgnoreCase));

        return new TorrentEntity
        {
            Title = title,
            AnimeTitle = animeTitle,
            Episode = episodeStr,
            Resolution = resolution,
            ReleaseGroup = group,
            DownloadLink = item.Element("link")?.Value ?? string.Empty,
            MagnetLink = !string.IsNullOrEmpty(infoHash)
                ? $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(title)}{AppConstants.Torrents.NyaaDefaultTrackersQuery}"
                : string.Empty,
            PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.UtcNow,
            Category = categoryStr,
            FormattedSize = sizeStr,
            Seeders = seeders,
            Leechers = leechers,
            Downloads = downloads,
            IsBatch = isBatch,
            IsNew = false
        };
    }
}
