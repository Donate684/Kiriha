using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Infrastructure.Http;

/// <summary>
/// Result envelope for <see cref="HttpConditionalCache.SendAsync"/>.
/// </summary>
/// <param name="Body">Response body bytes, or <c>null</c> on hard failure.</param>
/// <param name="StatusCode">HTTP status of the live response. <c>null</c> when
/// the body comes from a cache replay (304 hit or stale-on-error fallback).</param>
/// <param name="FromCache"><c>true</c> when <see cref="Body"/> was replayed
/// from the persisted cache (either via 304 or stale-on-error).</param>
public readonly record struct HttpCacheResult(byte[]? Body, HttpStatusCode? StatusCode, bool FromCache);

/// <summary>
/// Reusable HTTP conditional-GET wrapper backed by
/// <see cref="HttpCacheEntry"/> (table <c>http_response_cache</c>, 30 d TTL).
///
/// Usage: build a fresh <see cref="HttpRequestMessage"/> per call (with all
/// caller-specific headers — User-Agent, auth, etc.) inside
/// <c>requestFactory</c>. The helper attaches <c>If-None-Match</c> /
/// <c>If-Modified-Since</c> from the cache, sends the request, replays the
/// cached body on <c>304 Not Modified</c>, and persists fresh <c>200</c>
/// responses for next time.
///
/// Caveat: only call for endpoints whose body is safe to replay across the
/// same auth context (no per-call user-specific fields, or fields that the
/// caller already overrides downstream from a separate user store).
///
/// Persist is fire-and-forget — a transient DB hiccup costs at most one
/// extra full payload on the next call.
/// </summary>
public sealed class HttpConditionalCache
{
    private readonly HttpClient _http;
    private readonly IHttpCacheRepository _cache;
    private readonly string _logTag;
    private readonly Func<HttpClient, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

    /// <summary>Constructs a helper bound to the given <see cref="HttpClient"/>.</summary>
    /// <param name="http">Client used to send requests. The handler may add its own
    /// retry / resilience policy; that's orthogonal to this cache.</param>
    /// <param name="cache">Repository fronting the http_response_cache table.</param>
    /// <param name="logTag">Short tag used in log messages to identify the caller
    /// (e.g. <c>"MalApi"</c>, <c>"AniList"</c>). Saves grepping logs later.</param>
    public HttpConditionalCache(HttpClient http, IHttpCacheRepository cache, string logTag)
        : this(http, cache, logTag, static (client, request, ct) => client.SendAsync(request, ct))
    {
    }

    /// <summary>
    /// Constructs a helper with a custom send delegate. Intended for services
    /// that need request routing around <see cref="HttpClient.SendAsync"/>,
    /// such as Shikimori mirror resolution.
    /// </summary>
    public HttpConditionalCache(
        HttpClient http,
        IHttpCacheRepository cache,
        string logTag,
        Func<HttpClient, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        _http = http;
        _cache = cache;
        _logTag = logTag;
        _sendAsync = sendAsync;
    }

    /// <summary>
    /// Send a GET (or any safe verb) and return the response body bytes,
    /// replaying the cached body on 304. Returns <c>null</c> on a hard
    /// failure (4xx/5xx with no usable cache, or network error with no
    /// usable cache).
    /// </summary>
    /// <param name="requestFactory">Builds a fresh request (async to allow auth-token
    /// refresh inline). Called exactly once per invocation. Must set the absolute URI;
    /// cache key is derived from <see cref="HttpRequestMessage.RequestUri"/>.</param>
    /// <param name="throttle">Optional pre-send hook (e.g. per-service rate limiter).
    /// Awaited *after* the cache lookup but *before* the network call, so cache
    /// hits don't pay the throttle cost.</param>
    public async Task<byte[]?> SendAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<CancellationToken, Task>? throttle = null,
        CancellationToken ct = default,
        TimeSpan? localTtl = null)
    {
        var result = await SendForResultAsync(requestFactory, throttle, ct, localTtl);
        return result.Body;
    }

    /// <summary>
    /// Same as <see cref="SendAsync"/> but returns the full <see cref="HttpCacheResult"/>
    /// so callers can distinguish e.g. 404 (terminal — anime not on the source)
    /// from a transient network error.
    /// </summary>
    public async Task<HttpCacheResult> SendForResultAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<CancellationToken, Task>? throttle = null,
        CancellationToken ct = default,
        TimeSpan? localTtl = null)
    {
        using var request = await requestFactory(ct);
        var fullUrl = request.RequestUri?.ToString() ?? string.Empty;
        var urlHash = HashUrl(fullUrl);

        HttpCacheEntry? cached = null;
        try { cached = await _cache.GetAsync(urlHash); }
        catch (Exception ex) { Log.Debug(ex, "{Tag}: HTTP cache lookup failed for {Url}", _logTag, fullUrl); }

        if (cached != null)
        {
            if (localTtl.HasValue && DateTime.UtcNow - cached.CreatedAt < localTtl.Value)
            {
                Log.Debug("{Tag}: local TTL hit, serving cache for {Url}", _logTag, fullUrl);
                return new HttpCacheResult(cached.Body, null, FromCache: true);
            }

            // Both validators are independent — sending both lets the server
            // pick whichever it currently honours.
            if (!string.IsNullOrEmpty(cached.ETag))
            {
                try { request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cached.ETag)); }
                catch { /* malformed stored ETag — ignore and refetch */ }
            }
            if (!string.IsNullOrEmpty(cached.LastModified)
                && DateTimeOffset.TryParse(cached.LastModified, out var lm))
            {
                request.Headers.IfModifiedSince = lm;
            }
        }

        if (throttle != null) await throttle(ct);

        HttpResponseMessage response;
        try
        {
            response = await _sendAsync(_http, request, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Network blip — serve stale cache rather than failing the call.
            if (cached != null)
            {
                Log.Debug(ex, "{Tag}: network error, serving stale cache for {Url}", _logTag, fullUrl);
                return new HttpCacheResult(cached.Body, null, FromCache: true);
            }
            if (ex is HttpRequestException or TimeoutException || ex.InnerException is System.Net.Sockets.SocketException)
            {
                Log.Warning("{Tag}: Network request failed for {Url} ({Reason})", _logTag, fullUrl, ex.Message);
                Log.Debug(ex, "{Tag}: Detailed network failure for {Url}", _logTag, fullUrl);
            }
            else
            {
                Log.Warning(ex, "{Tag}: HttpConditionalCache send failed for {Url}", _logTag, fullUrl);
            }
            return new HttpCacheResult(null, null, FromCache: false);
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.NotModified && cached != null)
            {
                return new HttpCacheResult(cached.Body, HttpStatusCode.NotModified, FromCache: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 4xx/5xx without a usable replay — propagate the failure with
                // the status code so callers can react (e.g. 404 → sentinel).
                Log.Debug("{Tag}: {Url} returned {Status}", _logTag, fullUrl, response.StatusCode);
                return new HttpCacheResult(null, response.StatusCode, FromCache: false);
            }

            var body = await response.Content.ReadAsByteArrayAsync(ct);
            var etag = response.Headers.ETag?.Tag;
            var lmHeader = response.Content.Headers.LastModified?.ToString("R");

            // Persist asynchronously: caller doesn't need to wait. A failure here
            // just means the next call pays a full payload instead of 304.
            _ = Task.Run(async () =>
            {
                try { await _cache.UpsertAsync(urlHash, etag, lmHeader, body); }
                catch (Exception ex) { Log.Debug(ex, "{Tag}: failed to persist HTTP cache for {Url}", _logTag, fullUrl); }
            }, CancellationToken.None);

            return new HttpCacheResult(body, response.StatusCode, FromCache: false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private static string HashUrl(string url)
    {
        Span<byte> hash = stackalloc byte[32];
        int maxBytes = Encoding.UTF8.GetMaxByteCount(url.Length);
        if (maxBytes <= 512)
        {
            Span<byte> utf8 = stackalloc byte[maxBytes];
            int written = Encoding.UTF8.GetBytes(url, utf8);
            SHA256.HashData(utf8[..written], hash);
        }
        else
        {
            byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maxBytes);
            try
            {
                int written = Encoding.UTF8.GetBytes(url, rented);
                SHA256.HashData(rented.AsSpan(0, written), hash);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }

        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Updates the cached body for a URL in SQLite without clobbering its ETag/LastModified.
    /// Useful for persisting enriched responses (e.g. adding GraphQL posters to cached REST payloads).
    /// </summary>
    public async Task UpdateCachedBodyAsync(string fullUrl, byte[] newBody, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fullUrl) || newBody is null) return;
        var urlHash = HashUrl(fullUrl);
        try
        {
            var existing = await _cache.GetAsync(urlHash, ct);
            await _cache.UpsertAsync(urlHash, existing?.ETag, existing?.LastModified, newBody, ct);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "{Tag}: Failed to update cached body for {Url}", _logTag, fullUrl);
        }
    }

    /// <summary>
    /// Retrieves a cached payload directly by key.
    /// </summary>
    public async Task<byte[]?> GetCachedBodyAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var urlHash = HashUrl(key);
        try
        {
            var entry = await _cache.GetAsync(urlHash, ct);
            return entry?.Body;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "{Tag}: Cache lookup failed for key {Key}", _logTag, key);
            return null;
        }
    }

    /// <summary>
    /// Persists an arbitrary payload directly by key into SQLite http_response_cache.
    /// </summary>
    public async Task SetCachedBodyAsync(string key, byte[] body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key) || body is null) return;
        var urlHash = HashUrl(key);
        try
        {
            await _cache.UpsertAsync(urlHash, null, null, body, ct);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "{Tag}: Failed to set cached body for key {Key}", _logTag, key);
        }
    }
}
