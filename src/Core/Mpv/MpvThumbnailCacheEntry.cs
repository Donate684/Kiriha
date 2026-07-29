using System;

namespace Kiriha.Core.Mpv;

internal sealed class MpvThumbnailCacheEntry
{
    public MpvThumbnailCacheEntry(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
}
