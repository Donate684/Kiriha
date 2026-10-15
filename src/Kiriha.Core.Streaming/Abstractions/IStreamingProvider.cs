using Kiriha.Core.Streaming.Models;

namespace Kiriha.Core.Streaming.Abstractions;

/// <summary>
/// Provider that supplies anime catalog search, episode list, and player source links.
/// </summary>
public interface IStreamingProvider
{
    /// <summary>
    /// Provider name (e.g. "AnimeGo", "AniLibria").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Searches the catalog by title or keyword.
    /// </summary>
    Task<IReadOnlyList<StreamingSearchResult>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Fetches anime details and episode information.
    /// </summary>
    Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string animeUrlOrId, CancellationToken ct = default);

    /// <summary>
    /// Fetches player sources / dubs available for a specific episode.
    /// </summary>
    Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string animeId, int episodeNumber, CancellationToken ct = default);
}
