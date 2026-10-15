using Kiriha.Core.Streaming.Models;

namespace Kiriha.Core.Streaming.Abstractions;

/// <summary>
/// Unified service for searching across streaming providers and resolving embed video streams.
/// </summary>
public interface IStreamingService
{
    /// <summary>
    /// Registered catalog providers.
    /// </summary>
    IReadOnlyList<IStreamingProvider> Providers { get; }

    /// <summary>
    /// Registered embed player extractors.
    /// </summary>
    IReadOnlyList<IVideoExtractor> Extractors { get; }

    /// <summary>
    /// Searches across registered providers concurrently. Optionally filtered by enabled provider names.
    /// </summary>
    Task<IReadOnlyList<StreamingSearchResult>> SearchAllAsync(string query, IEnumerable<string>? enabledProviders = null, CancellationToken ct = default);

    /// <summary>
    /// Searches across registered providers concurrently, streaming results progressively as each provider completes.
    /// Optionally filtered by enabled provider names.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<StreamingSearchResult>> SearchStreamAsync(string query, IEnumerable<string>? enabledProviders = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches anime details and episode list from a specific provider.
    /// </summary>
    Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string providerName, string animeUrlOrId, CancellationToken ct = default);

    /// <summary>
    /// Fetches player sources / dubs available for an episode from a specific provider.
    /// </summary>
    Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string providerName, string animeId, int episodeNumber, CancellationToken ct = default);

    /// <summary>
    /// Resolves an embed player URL or direct stream link into playable video streams.
    /// </summary>
    Task<IReadOnlyList<StreamingVideo>> ResolveStreamAsync(string playerUrl, CancellationToken ct = default);
}

