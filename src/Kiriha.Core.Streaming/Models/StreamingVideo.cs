namespace Kiriha.Core.Streaming.Models;

/// <summary>
/// Direct playable video stream link with metadata and headers required for playback.
/// </summary>
public sealed record StreamingVideo
{
    /// <summary>
    /// Direct stream URL (HLS .m3u8, MP4, or DASH .mpd).
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Stream container / protocol type ("m3u8", "mp4", "mpd").
    /// </summary>
    public required string VideoType { get; init; }

    /// <summary>
    /// Video vertical resolution (e.g. 1080, 720, 480, 360).
    /// </summary>
    public int Quality { get; init; }

    /// <summary>
    /// HTTP headers required by the CDN or stream provider (e.g. Referer, Origin, User-Agent).
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// External subtitle track URL (ASS, VTT, SRT) associated with the stream if available.
    /// </summary>
    public string? SubtitleUrl { get; init; }
}
