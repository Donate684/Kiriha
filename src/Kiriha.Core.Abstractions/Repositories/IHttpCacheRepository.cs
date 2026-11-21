using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the conditional-GET HTTP cache (the
/// <c>http_response_cache</c> table). Stores <c>ETag</c> / <c>Last-Modified</c>
/// alongside the body keyed by URL hash so subsequent requests can validate
/// cheaply via <c>If-None-Match</c> / <c>If-Modified-Since</c>.
///
/// Entries expire 60 days after successful validation. The validation
/// timestamp can be refreshed independently of the response body.
/// </summary>
public interface IHttpCacheRepository
{
    Task<HttpCacheEntry?> GetAsync(string urlHash, CancellationToken ct = default);

    Task UpsertAsync(string urlHash, string? etag, string? lastModified, byte[] body, CancellationToken ct = default);

    Task TouchAsync(string urlHash, CancellationToken ct = default) => Task.CompletedTask;

    Task RemoveAsync(string urlHash, CancellationToken ct = default) => Task.CompletedTask;
}
