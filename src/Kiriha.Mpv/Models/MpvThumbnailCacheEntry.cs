using System;

namespace Kiriha.Mpv;

internal sealed class MpvThumbnailCacheEntry
{
    public MpvThumbnailCacheEntry(MpvThumbnailFrame frame)
    {
        Frame = frame;
    }

    public MpvThumbnailFrame Frame { get; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
}
