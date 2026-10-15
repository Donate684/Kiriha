using Kiriha.Core.Streaming.Models;

namespace Kiriha.Core.Streaming.Abstractions;

/// <summary>
/// Extracts direct video streams (HLS, MP4, DASH) from embed player URLs (e.g. Kodik, Aniboom, Sibnet).
/// </summary>
public interface IVideoExtractor
{
    /// <summary>
    /// Extractor name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines whether this extractor can resolve videos from the specified player URL.
    /// </summary>
    bool CanExtract(string url);

    /// <summary>
    /// Resolves direct video streams from the player URL.
    /// </summary>
    Task<IReadOnlyList<StreamingVideo>> ExtractVideosAsync(string playerUrl, CancellationToken ct = default);
}
