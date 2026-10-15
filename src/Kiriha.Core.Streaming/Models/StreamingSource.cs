namespace Kiriha.Core.Streaming.Models;

/// <summary>
/// Source player or dubbing option for an episode.
/// </summary>
public sealed record StreamingSource
{
    /// <summary>
    /// Display title (e.g. "Студийная Банда", "AniLibria", "Aniboom").
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Player embed URL or direct identifier for video extraction.
    /// </summary>
    public required string PlayerUrl { get; init; }

    /// <summary>
    /// Name of voice studio or dubber if provided.
    /// </summary>
    public string? Dubber { get; init; }

    /// <summary>
    /// Underlying player engine or provider name (e.g. "Kodik", "Aniboom", "Sibnet", "AniLibria").
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Video resolution / quality tag if known (e.g. "1080p", "720p").
    /// </summary>
    public string? Quality { get; init; }

    /// <summary>
    /// Language code or tag if known (e.g. "ru", "en", "ja").
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// External subtitle track URL (ASS, VTT, SRT) if provided separately from video stream.
    /// </summary>
    public string? SubtitleUrl { get; init; }
}
