namespace Kiriha.Core.Domain.Models.Entities;

public class TorrentEntity
{
    private string _title = string.Empty;
    private string _releaseGroup = string.Empty;
    private string? _displayTitle;

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            _displayTitle = null;
        }
    }

    public string AnimeTitle { get; set; } = string.Empty;
    public string Episode { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;

    public string ReleaseGroup
    {
        get => _releaseGroup;
        set
        {
            _releaseGroup = value;
            _displayTitle = null;
        }
    }

    public string DisplayTitle => _displayTitle ??= ComputeDisplayTitle();

    private string ComputeDisplayTitle()
    {
        if (string.IsNullOrWhiteSpace(_title))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(_releaseGroup))
        {
            var parts = _releaseGroup.Trim().Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
            var escapedGroup = parts.Length > 0
                ? string.Join(@"[-_\s]*", parts.Select(System.Text.RegularExpressions.Regex.Escape))
                : System.Text.RegularExpressions.Regex.Escape(_releaseGroup.Trim());

            // 1. Try stripping from start: ^\s*[\[\(]Group[\]\)]\s*
            var startPattern = @"^\s*[\[\(]" + escapedGroup + @"[\]\)]\s*";
            var cleaned = System.Text.RegularExpressions.Regex.Replace(_title, startPattern, string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (cleaned != _title && !string.IsNullOrWhiteSpace(cleaned))
                return cleaned.Trim();

            // 2. If not at start, try stripping [Group] or (Group) anywhere
            var anyPattern = @"[\[\(]" + escapedGroup + @"[\]\)]\s*";
            cleaned = System.Text.RegularExpressions.Regex.Replace(_title, anyPattern, string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!string.IsNullOrWhiteSpace(cleaned))
                return cleaned.Trim();
        }

        return _title;
    }
    public string Quality { get; set; } = string.Empty;
    public string Subber { get; set; } = string.Empty;
    public string DownloadLink { get; set; } = string.Empty;
    public string MagnetLink { get; set; } = string.Empty;
    public DateTime PublishDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public long Size { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int Downloads { get; set; }
    public bool IsNew { get; set; }
    public bool IsMatched { get; set; }
    public string FormattedSize { get; set; } = string.Empty;
    public bool IsBatch { get; set; }
    public bool Is1080p => Resolution.Contains("1080", StringComparison.OrdinalIgnoreCase);
    public bool IsOtherResolution => !string.IsNullOrEmpty(Resolution) && !Is1080p;
    public bool ShowEpisodeBadge => !IsBatch && !string.IsNullOrEmpty(Episode);
    public bool HasSeeders => Seeders > 0;
}
