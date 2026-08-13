using System;
using System.Threading.Tasks;
using Kiriha.Models.Entities;

namespace Kiriha.Core.Repositories;

/// <summary>
/// Persistence boundary for the titleâ†’MAL-id resolution cache (the
/// <c>mal_search_cache</c> table). Built on top of MAL's title search to skip
/// the round-trip when the same window-title text has already been resolved
/// recently.
///
/// TTL policy lives here, not at the call site:
///   * positive resolutions (anime_id != 0) â€” 30 days
///   * negative resolutions (anime_id == 0) â€” 7 days, since titles we couldn't
///     match might appear on MAL later (newly added entries) and we want to
///     retry sooner.
/// </summary>
public interface IMalSearchCacheRepository
{
    /// <summary>Returns a non-expired cache hit, or null on miss / expired entry.</summary>
    Task<MalSearchCache?> GetAsync(string queryNormalized);

    Task UpsertAsync(string queryNormalized, int animeId, float score);
}
