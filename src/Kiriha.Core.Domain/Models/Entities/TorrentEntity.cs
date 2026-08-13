using Kiriha.Core.Domain.Models.Entities;
using System;

namespace Kiriha.Core.Domain.Models.Entities;

public class TorrentEntity
{
    public string Title { get; set; } = string.Empty;
    public string AnimeTitle { get; set; } = string.Empty;
    public string Episode { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string ReleaseGroup { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Subber { get; set; } = string.Empty;
    public string TorrentUrl { get; set; } = string.Empty;
    public string DownloadLink { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string MagnetUri { get; set; } = string.Empty;
    public string MagnetLink { get; set; } = string.Empty;
    public DateTime PubDate { get; set; }
    public DateTime PublishDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public long Size { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int Downloads { get; set; }
    public bool IsNew { get; set; }
    public bool IsMatched { get; set; }
}
