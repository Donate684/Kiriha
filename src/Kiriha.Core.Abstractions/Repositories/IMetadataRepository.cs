using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for Shikimori metadata (the <c>metadata</c> table),
/// keyed by composite <c>(Id, MediaKind)</c>.
/// </summary>
public interface IMetadataRepository
{
    /// <summary>Returns the cached entry for <c>(id, mediaKind)</c>, or null if never fetched.</summary>
    Task<ShikiMetadata?> GetAsync(int id, MediaKind mediaKind = MediaKind.Anime, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates the entry. <see cref="ShikiMetadata.FetchedAt"/> is
    /// stamped to <see cref="DateTime.UtcNow"/> unconditionally so the TTL
    /// window is reset on every successful upsert.
    /// </summary>
    Task UpsertAsync(ShikiMetadata meta, CancellationToken ct = default);

    /// <summary>Returns cached metadata for the requested <see cref="MediaKey"/>s in a single batch query.</summary>
    Task<Dictionary<MediaKey, ShikiMetadata>> GetBatchAsync(IEnumerable<MediaKey> keys, CancellationToken ct = default);
}
