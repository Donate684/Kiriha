using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Data.Repository;

/// <summary>
/// Persistence boundary for episode release lists (the <c>episode_releases</c>
/// table) and their freshness sidecar (<c>episode_list_meta</c>). Episodes and
/// their fetch timestamp are written together in <see cref="ReplaceAsync"/>
/// so freshness gates can rely on a single atomic boundary instead of two
/// independent rows that could disagree after a crash.
/// </summary>
public interface IEpisodeReleaseRepository
{
    Task<List<EpisodeRelease>> GetByMalIdAsync(int malId);

    /// <summary>UTC timestamp of the last successful fetch, or null on miss.</summary>
    Task<DateTime?> GetFetchedAtAsync(int malId);

    /// <summary>
    /// Replaces the entire episode list for <paramref name="malId"/> and stamps
    /// <see cref="EpisodeListMeta.FetchedAt"/> in the same SaveChanges call.
    /// </summary>
    Task ReplaceAsync(int malId, IEnumerable<EpisodeRelease> episodes);
}
