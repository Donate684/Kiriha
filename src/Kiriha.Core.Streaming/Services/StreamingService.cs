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
    private readonly TimeSpan _searchTimeout;
    private readonly TimeSpan _requestTimeout;

    public IReadOnlyList<IStreamingProvider> Providers => _providers;
    public IReadOnlyList<IVideoExtractor> Extractors => _extractors;

    public StreamingService(
        IEnumerable<IStreamingProvider> providers,
        IEnumerable<IVideoExtractor> extractors)
        : this(providers, extractors,
            TimeSpan.FromSeconds(StreamingConstants.Timeouts.ProviderSearchTimeoutSeconds),
            TimeSpan.FromSeconds(StreamingConstants.Timeouts.ProviderRequestTimeoutSeconds))
    {
    }

    internal StreamingService(IEnumerable<IStreamingProvider> providers, IEnumerable<IVideoExtractor> extractors,
        TimeSpan searchTimeout, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(extractors);

        _providers = [.. providers];
        _extractors = [.. extractors];
        _searchTimeout = searchTimeout;
        _requestTimeout = requestTimeout;
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
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var eligibleProviders = enabledProviders != null
            ? _providers.Where(p => enabledProviders.Contains(p.ProviderName, StringComparer.OrdinalIgnoreCase)).ToList()
            : _providers;

        if (eligibleProviders.Count == 0)
            yield break;

        using var search = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = eligibleProviders.Select(p => SearchProviderAsync(p, query, search.Token)).ToList();
        try
        {
            while (tasks.Count > 0)
            {
                var finished = await Task.WhenAny(tasks).WaitAsync(ct).ConfigureAwait(false);
                tasks.Remove(finished);
                var items = await finished.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (items.Count > 0)
                    yield return items;
            }
        }
        finally
        {
            await search.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<StreamingSearchResult>> SearchProviderAsync(
        IStreamingProvider provider, string query, CancellationToken ct)
    {
        try
        {
            return await RunWithDeadlineAsync(token => provider.SearchAsync(query, token), _searchTimeout, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The enumerator owns cancellation. Complete this worker even if the consumer stops enumerating.
            return [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[StreamingService] Provider {Provider} failed during search for '{Query}'", provider.ProviderName, query);
            return [];
        }
    }

    public async Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string providerName, string animeUrlOrId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(providerName);
        ArgumentNullException.ThrowIfNull(animeUrlOrId);

        var provider = _providers.FirstOrDefault(p => string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        if (provider == null) return null;

        return await RunWithDeadlineAsync(token => provider.GetAnimeDetailsAsync(animeUrlOrId, token), _requestTimeout, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string providerName, string animeId, int episodeNumber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(providerName);
        ArgumentNullException.ThrowIfNull(animeId);

        var provider = _providers.FirstOrDefault(p => string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        if (provider == null) return [];

        return await RunWithDeadlineAsync(token => provider.GetEpisodeSourcesAsync(animeId, episodeNumber, token), _requestTimeout, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StreamingVideo>> ResolveStreamAsync(string playerUrl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!StreamingMediaUrl.TryParse(playerUrl, out var playerUri))
            return [];

        playerUrl = StreamingMediaUrl.Normalize(playerUrl);

        foreach (var extractor in _extractors)
        {
            try
            {
                if (!extractor.CanExtract(playerUrl))
                    continue;

                var videos = await RunWithDeadlineAsync(token => extractor.ExtractVideosAsync(playerUrl, token), _requestTimeout, ct)
                    .ConfigureAwait(false);
                var playable = videos.Where(StreamingMediaUrl.IsPlayable)
                    .Select(video => video with { Url = StreamingMediaUrl.Normalize(video.Url) }).ToList();
                if (playable.Count > 0)
                    return playable;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ct.ThrowIfCancellationRequested();
                Log.Warning(ex, "[StreamingService] Extractor {Extractor} failed for {Url}", extractor.Name, Helpers.UrlRedactor.Redact(playerUrl));
            }
        }

        ct.ThrowIfCancellationRequested();
        if (StreamingMediaUrl.GetFileType(playerUri) is { } type)
        {
            return [new StreamingVideo { Url = playerUrl, Quality = type == "m3u8" ? 1080 : 720, VideoType = type }];
        }

        Log.Warning("[StreamingService] No suitable extractor found to resolve {Url}", Helpers.UrlRedactor.Redact(playerUrl));
        return [];
    }

    private static async Task<T> RunWithDeadlineAsync<T>(Func<CancellationToken, Task<T>> run, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var request = run(deadline.Token);
        try
        {
            var result = await request.WaitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            // The provider may complete or fail after we stop waiting.
            _ = ObserveCompletionAsync(request);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("The streaming provider did not respond in time.", ex);
        }
    }

    private static async Task ObserveCompletionAsync(Task operation) =>
        await operation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
}
