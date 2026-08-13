using System;
using System.Threading.Tasks;
using Kiriha.Models.Entities;

namespace Kiriha.Core.Repositories;

/// <summary>
/// Persistence boundary for the conditional-GET HTTP cache (the
/// <c>http_response_cache</c> table). Stores <c>ETag</c> / <c>Last-Modified</c>
/// alongside the body keyed by URL hash so subsequent requests can validate
/// cheaply via <c>If-None-Match</c> / <c>If-Modified-Since</c>.
///
/// TTL: 30 days hard-stop for entries the server stopped revalidating. Lookups
/// re-validate against the origin on every call, so even "old" cache entries
/// remain correct as long as the server confirms them via 304.
/// </summary>
public interface IHttpCacheRepository
{
    Task<HttpCacheEntry?> GetAsync(string urlHash);

    Task UpsertAsync(string urlHash, string? etag, string? lastModified, byte[] body);
}
