using System.Runtime.CompilerServices;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Serilog;

namespace Kiriha.Core.Streaming.Services;

/// <summary>
/// Orchestrates search across multiple streaming providers and resolves embed player URLs to direct playable streams.
/// </summary>
public sealed class StreamingService : IStreamingService
{
    private readonly IReadOnlyList<IStreamingProvider> _providers;
    private readonly IReadOnlyList<IVideoExtractor> _extractors;

    public IReadOnlyList<IStreamingProvider> Providers => _providers;
    public IReadOnlyList<IVideoExtractor> Extractors => _extractors;

    public StreamingService(
        IEnumerable<IStreamingProvider> providers,
        IEnumerable<IVideoExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(extractors);

        _providers = [.. providers];
        _extractors = [.. extractors];
    }

    public async Task<IReadOnlyList<StreamingSearchResult>> SearchAllAsync(string query, IEnumerable<string>? enabledProviders = null, CancellationToken ct = default)
    {
        var merged = new List<StreamingSearchResult>();
        await foreach (var batch in SearchStreamAsync(query, enabledProviders, ct).ConfigureAwait(false))
        {
            merged.AddRange(batch);
        }
        return merged;
    }

    public async IAsyncEnumerable<IReadOnlyList<StreamingSearchResult>> SearchStreamAsync(
        string query,
        IEnumerable<string>? enabledProviders = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var eligibleProviders = enabledProviders != null
            ? _providers.Where(p => enabledProviders.Contains(p.ProviderName, StringComparer.OrdinalIgnoreCase)).ToList()
            : _providers;

        if (eligibleProviders.Count == 0)
            yield break;

        var tasks = eligibleProviders.Select(async p =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(StreamingConstants.Timeouts.ProviderSearchTimeoutSeconds));
                return await p.SearchAsync(query, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[StreamingService] Provider {Provider} failed during search for '{Query}'", p.ProviderName, query);
                return (IReadOnlyList<StreamingSearchResult>)[];
            }
        }).ToList();

        while (tasks.Count > 0)
        {
            var finished = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(finished);

            IReadOnlyList<StreamingSearchResult> items;
            try
            {
                items = await finished.ConfigureAwait(false);
            }
            catch
            {
                items = [];
            }

            if (items.Count > 0)
            {
                yield return items;
            }
        }
    }

    public async Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string providerName, string animeUrlOrId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(providerName);
        ArgumentNullException.ThrowIfNull(animeUrlOrId);

        var provider = _providers.FirstOrDefault(p => string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        if (provider == null) return null;

        return await provider.GetAnimeDetailsAsync(animeUrlOrId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string providerName, string animeId, int episodeNumber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(providerName);
        ArgumentNullException.ThrowIfNull(animeId);

        var provider = _providers.FirstOrDefault(p => string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        if (provider == null) return [];

        return await provider.GetEpisodeSourcesAsync(animeId, episodeNumber, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StreamingVideo>> ResolveStreamAsync(string playerUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playerUrl))
            return [];

        foreach (var extractor in _extractors)
        {
            if (extractor.CanExtract(playerUrl))
            {
                try
                {
                    var videos = await extractor.ExtractVideosAsync(playerUrl, ct).ConfigureAwait(false);
                    if (videos.Count > 0)
                        return videos;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[StreamingService] Extractor {Extractor} failed for {Url}", extractor.Name, playerUrl);
                }
            }
        }

        // Direct stream URL fallback (e.g. .m3u8 or .mp4 URL provided directly)
        if (playerUrl.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return [
                new StreamingVideo
                {
                    Url = playerUrl,
                    Quality = 1080,
                    VideoType = "m3u8"
                }
            ];
        }

        if (playerUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return [
                new StreamingVideo
                {
                    Url = playerUrl,
                    Quality = 720,
                    VideoType = "mp4"
                }
            ];
        }

        Log.Warning("[StreamingService] No suitable extractor found to resolve {Url}", playerUrl);
        return [];
    }
}
