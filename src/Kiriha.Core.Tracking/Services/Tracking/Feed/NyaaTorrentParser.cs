using System.Xml.Linq;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Feed;

internal static class NyaaTorrentParser
{
    public static TorrentEntity? ParseItem(XElement item)
    {
        string? title = item.Element("title")?.Value;
        if (string.IsNullOrEmpty(title)) return null;

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
        var animeTitle = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
        var episodeStr = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;
        var resolution = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementVideoResolution)?.Value;
        var group = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementReleaseGroup)?.Value;

        var nyaaNs = XNamespace.Get(Kiriha.Core.Domain.Constants.AppConstants.Api.Nyaa.XmlNamespace);
        var infoHash = item.Element(nyaaNs + "infoHash")?.Value;

        return new TorrentEntity
        {
            Title = title,
            AnimeTitle = animeTitle ?? string.Empty,
            Episode = episodeStr ?? string.Empty,
            Resolution = resolution ?? string.Empty,
            ReleaseGroup = group ?? string.Empty,
            DownloadLink = item.Element("link")?.Value ?? string.Empty,
            MagnetLink = !string.IsNullOrEmpty(infoHash) ? $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(title)}" : string.Empty,
            PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.UtcNow,
            IsNew = false
        };
    }
}
