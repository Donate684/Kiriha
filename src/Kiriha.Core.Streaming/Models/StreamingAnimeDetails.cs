namespace Kiriha.Core.Streaming.Models;

/// <summary>
/// Detailed metadata and episode list for a specific anime from a streaming provider.
/// </summary>
public sealed record StreamingAnimeDetails
{
    /// <summary>
    /// Provider-specific anime identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Anime title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Anime synopsis or description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Poster or thumbnail URL.
    /// </summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// Provider name that returned this details object.
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Available episodes.
    /// </summary>
    public IReadOnlyList<StreamingEpisode> Episodes { get; init; } = [];
}
