using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the user's anime list (the <c>user_anime</c> table).
/// Owns full-list synchronisation, point reads/writes, and deletes; intentionally
/// does NOT touch sync tasks or history — those live in their own repositories
/// (<see cref="ISyncTaskRepository"/>, <see cref="IHistoryRepository"/>) so a
/// future move to a different store (e.g. server-backed) can be done one
/// aggregate at a time.
///
/// Lifetime: singleton. The underlying <see cref="IDbContextFactory{TContext}"/>
/// makes every method create a fresh DbContext, so there is no shared mutable
/// state between calls.
/// </summary>
public interface IUserAnimeRepository
{
    Task<List<AnimeEntity>> GetAllAsync(CancellationToken ct = default);
    Task<List<AnimeEntity>> GetByMediaKindAsync(MediaKind kind, CancellationToken ct = default);
    Task UpsertAsync(AnimeEntity item, CancellationToken ct = default);
    Task UpdateAsync(AnimeEntity item, CancellationToken ct = default);
    Task UpdateProgressAsync(AnimeEntity item, int progress, UserAnimeStatus? status = null, CancellationToken ct = default);
    Task UpdateScoreAsync(AnimeEntity item, string score, CancellationToken ct = default);
    Task UpdateMetadataAsync(AnimeEntity item, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Mirrors a remote tracker snapshot into the local table: upserts items
    /// that exist remotely, deletes items that don't. Refuses to delete a
    /// non-empty local list when the incoming list is empty (defensive against
    /// transient API failures returning an empty body).
    /// </summary>
    Task SyncFromRemoteAsync(IEnumerable<AnimeEntity> items, MediaKind[]? syncKinds = null, CancellationToken ct = default);

    /// <summary>Local poster paths for items currently tracked. Used by image cache cleanup.</summary>
    Task<List<string>> GetActiveLocalImagePathsAsync(CancellationToken ct = default);
}
