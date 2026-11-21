using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

/// <summary>Body and origin status of a cached or live HTTP response.</summary>
public readonly record struct HttpCacheResult(byte[]? Body, HttpStatusCode? StatusCode, bool FromCache,
    bool IsStaleFallback = false, bool BodyChanged = false);

/// <summary>Conditional requests for public payloads with optional local freshness and shared refreshes.</summary>
public sealed class HttpConditionalCache
{
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinimumValidationWriteInterval = TimeSpan.FromMinutes(15);
    private readonly HttpClient _http;
    private readonly IHttpCacheRepository _cache;
    private readonly string _logTag;
    private readonly TimeProvider _clock;
    private readonly Func<HttpClient, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;
    private static readonly ConditionalWeakTable<IHttpCacheRepository, SharedAsyncWork<(string Key, TimeSpan? Freshness), HttpCacheResult>> SharedRefreshes = new();
    private readonly SharedAsyncWork<(string Key, TimeSpan? Freshness), HttpCacheResult> _refreshes;

    public HttpConditionalCache(HttpClient http, IHttpCacheRepository cache, string logTag, TimeProvider? clock = null)
        : this(http, cache, logTag, static (client, request, ct) => client.SendAsync(request, ct), clock)
    {
    }

    public HttpConditionalCache(
        HttpClient http, IHttpCacheRepository cache, string logTag,
        Func<HttpClient, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        TimeProvider? clock = null)
    {
        _http = http;
        _cache = cache;
        _logTag = logTag;
        _sendAsync = sendAsync;
        _clock = clock ?? TimeProvider.System;
        _refreshes = SharedRefreshes.GetValue(cache, static _ => new());
    }

    public async Task<byte[]?> SendAsync(
        string url, Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<CancellationToken, Task>? throttle = null, CancellationToken ct = default, TimeSpan? localTtl = null) =>
        (await SendForResultAsync(url, requestFactory, throttle, ct, localTtl)).Body;

    public async Task<byte[]?> SendAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<CancellationToken, Task>? throttle = null, CancellationToken ct = default, TimeSpan? localTtl = null) =>
        (await SendForResultAsync(requestFactory, throttle, ct, localTtl)).Body;

    /// <summary>The URL determines the cache key before request construction, token refresh, or throttling.</summary>
    public Task<HttpCacheResult> SendForResultAsync(
        string url, Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<CancellationToken, Task>? throttle = null, CancellationToken ct = default, TimeSpan? localTtl = null,
        Func<byte[], TimeSpan?>? getFreshness = null)
    {
        var key = HashUrl(new Uri(url, UriKind.Absolute).AbsoluteUri);
        return _refreshes.RunAsync((key, localTtl), async () =>
        {
            using var deadline = new CancellationTokenSource(RequestBudget);
            var token = deadline.Token;
            HttpCacheEntry? cached = null;
            try
            {
                cached = await ReadAsync(key, token);
                var ttl = localTtl;
                if (cached is { Body.Length: > 0 } && getFreshness is not null)
                {
                    ttl = getFreshness(cached.Body);
                    if (ttl is null) cached = null;
                }
                if (IsFresh(cached, ttl))
                    return new HttpCacheResult(cached?.Body, null, FromCache: true);

                using var request = await requestFactory(token);
                return await RefreshAsync(key, request, cached, throttle, ttl, token, getFreshness);
            }
            catch (OperationCanceledException ex) when (token.IsCancellationRequested)
            {
                Log.Debug(ex, "{Tag}: HTTP cache refresh timed out", _logTag);
                return StaleOrFailure(cached, null);
            }
        }, ct);
    }

    public async Task<HttpCacheResult> SendForResultAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<CancellationToken, Task>? throttle = null, CancellationToken ct = default, TimeSpan? localTtl = null)
    {
        using var request = await requestFactory(ct);
        var key = HashUrl(request.RequestUri?.AbsoluteUri ?? string.Empty);
        var cached = await ReadAsync(key, ct);
        return IsFresh(cached, localTtl)
            ? new HttpCacheResult(cached?.Body, null, FromCache: true)
            : await RefreshAsync(key, request, cached, throttle, localTtl, ct);
    }

    private bool IsFresh(HttpCacheEntry? cached, TimeSpan? ttl) =>
        cached is { Body.Length: > 0 } && ttl is { } age && Age(cached) < age;

    private TimeSpan Age(HttpCacheEntry cached) => _clock.GetUtcNow().UtcDateTime - cached.CreatedAt;

    internal async Task<(byte[]? Body, TimeSpan Age)> ReadCachedResponseAsync(string url, CancellationToken ct)
    {
        var cached = await ReadAsync(HashUrl(new Uri(url, UriKind.Absolute).AbsoluteUri), ct);
        return cached is null ? (null, default) : (cached.Body, Age(cached));
    }

    private async Task<HttpCacheResult> RefreshAsync(string key, HttpRequestMessage request, HttpCacheEntry? cached,
        Func<CancellationToken, Task>? throttle, TimeSpan? localTtl, CancellationToken ct,
        Func<byte[], TimeSpan?>? getFreshness = null)
    {
        ApplyValidators(request, cached);
        if (throttle != null)
            await throttle(ct);

        try
        {
            using var response = await _sendAsync(_http, request, ct);
            var status = response.StatusCode;
            var cacheControl = response.Headers.CacheControl;
            bool requiresNetwork = cacheControl is { NoStore: true } or { NoCache: true };
            if (status == HttpStatusCode.NotModified && cached != null)
            {
                if (requiresNetwork)
                    await TryWriteAsync(() => _cache.RemoveAsync(key, ct));
                else
                    await RefreshTimestampAsync(key, cached, localTtl, ct);
                return new HttpCacheResult(cached.Body, status, FromCache: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                if ((int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                    return StaleOrFailure(cached, status);
                if (cached != null && status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    await TryWriteAsync(() => _cache.RemoveAsync(key, ct));
                return new HttpCacheResult(null, status, FromCache: false);
            }

            var body = await response.Content.ReadAsByteArrayAsync(ct);
            if (status == HttpStatusCode.OK && getFreshness is not null && getFreshness(body) is null)
                return StaleOrFailure(cached, status);

            bool bodyChanged = cached is null || !cached.Body.AsSpan().SequenceEqual(body);
            if (requiresNetwork && cached != null)
                await TryWriteAsync(() => _cache.RemoveAsync(key, ct));
            if (status == HttpStatusCode.OK && body.Length != 0 && !requiresNetwork)
            {
                var etag = response.Headers.ETag?.ToString();
                var modified = response.Content.Headers.LastModified?.ToString("R");
                if (cached != null && etag == cached.ETag && modified == cached.LastModified && !bodyChanged)
                {
                    await RefreshTimestampAsync(key, cached, localTtl, ct);
                }
                else
                {
                    await TryWriteAsync(() => _cache.UpsertAsync(key, etag, modified, body, ct));
                }
            }

            return new HttpCacheResult(body, status, FromCache: false, BodyChanged: bodyChanged);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Debug(ex, "{Tag}: HTTP request failed", _logTag);
            return StaleOrFailure(cached, null);
        }
    }

    private Task RefreshTimestampAsync(string key, HttpCacheEntry entry, TimeSpan? ttl, CancellationToken ct) =>
        Age(entry) >= (ttl.GetValueOrDefault() > TimeSpan.Zero ? ttl.GetValueOrDefault() : MinimumValidationWriteInterval)
            ? TryWriteAsync(() => _cache.TouchAsync(key, ct))
            : Task.CompletedTask;

    private static HttpCacheResult StaleOrFailure(HttpCacheEntry? cached, HttpStatusCode? status) =>
        cached is { Body.Length: > 0 }
            ? new HttpCacheResult(cached.Body, status, FromCache: true, IsStaleFallback: true)
            : new HttpCacheResult(null, status, FromCache: false);

    private static void ApplyValidators(HttpRequestMessage request, HttpCacheEntry? entry)
    {
        if (EntityTagHeaderValue.TryParse(entry?.ETag, out var tag))
            request.Headers.IfNoneMatch.Add(tag);
        if (DateTimeOffset.TryParse(entry?.LastModified, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal, out var modified))
        {
            request.Headers.IfModifiedSince = modified;
        }
    }

    private async Task<HttpCacheEntry?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            return await _cache.GetAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug(ex, "{Tag}: HTTP cache lookup failed", _logTag);
            return null;
        }
    }

    private async Task TryWriteAsync(Func<Task> persist)
    {
        try
        {
            await persist();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug(ex, "{Tag}: HTTP cache write failed", _logTag);
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

    public async Task UpdateCachedBodyAsync(string fullUrl, byte[] newBody, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fullUrl) || newBody is null) return;
        var key = HashUrl(new Uri(fullUrl, UriKind.Absolute).AbsoluteUri);
        var existing = await ReadAsync(key, ct);
        if (existing == null || !existing.Body.AsSpan().SequenceEqual(newBody))
            await TryWriteAsync(() => _cache.UpsertAsync(key, existing?.ETag, existing?.LastModified, newBody, ct));
    }

    public async Task<byte[]?> GetCachedBodyAsync(string key, CancellationToken ct = default) =>
        string.IsNullOrEmpty(key) ? null : (await ReadAsync(HashUrl(key), ct))?.Body;

    public async Task SetCachedBodyAsync(string key, byte[] body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key) || body is null) return;
        var hash = HashUrl(key);
        var existing = await ReadAsync(hash, ct);
        if (existing == null || !existing.Body.AsSpan().SequenceEqual(body))
            await TryWriteAsync(() => _cache.UpsertAsync(hash, null, null, body, ct));
    }
}
