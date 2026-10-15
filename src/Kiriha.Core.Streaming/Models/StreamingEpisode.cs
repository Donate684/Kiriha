namespace Kiriha.Core.Streaming.Models;

/// <summary>
/// Episode item with available player sources and dubs.
/// </summary>
public sealed record StreamingEpisode
{
    /// <summary>
    /// Sequential episode number.
    /// </summary>
    public required int EpisodeNumber { get; init; }

    /// <summary>
    /// Episode title or name.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Episode identifier in provider's system (if applicable).
    /// </summary>
    public string? EpisodeId { get; init; }

    /// <summary>
    /// Available player sources for this episode.
    /// </summary>
    public IReadOnlyList<StreamingSource> Sources { get; init; } = [];
}
