namespace Kiriha.Core.Streaming.Models;

/// <summary>
/// Anime search result item from an online streaming catalog.
/// </summary>
public sealed record StreamingSearchResult
{
    /// <summary>
    /// Identifier or URL key used to retrieve details.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Anime title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Poster thumbnail URL.
    /// </summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// Relative or absolute URL to anime page.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Source provider name (e.g. "AnimeGo", "AniLibria").
    /// </summary>
    public required string ProviderName { get; init; }
}
