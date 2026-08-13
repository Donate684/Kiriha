using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models.Api;

namespace Kiriha.Core.Repositories;

/// <summary>
/// Persistence boundary for Shikimori metadata (the <c>metadata</c> table).
/// Stores the localised titles / synopses / studios fetched from the Shiki API
/// keyed by MAL id (Shiki and MAL share ids for matched titles).
/// </summary>
public interface IMetadataRepository
{
    /// <summary>Returns the cached entry, or null if we've never fetched it.</summary>
    Task<ShikiMetadata?> GetAsync(int id);

    /// <summary>
    /// Inserts or updates the entry. <see cref="ShikiMetadata.FetchedAt"/> is
    /// stamped to <see cref="DateTime.UtcNow"/> unconditionally so the TTL
    /// window is reset on every successful upsert.
    /// </summary>
    Task UpsertAsync(ShikiMetadata meta);

    /// <summary>Returns a set of all currently cached metadata IDs.</summary>
    Task<HashSet<int>> GetAllIdsAsync();
}
