using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

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
    Task<List<AnimeItem>> GetAllAsync();
    Task<List<AnimeItem>> GetByMediaKindAsync(MediaKind kind);
    Task UpsertAsync(AnimeItem item);
    Task UpdateAsync(AnimeItem item);
    Task UpdateProgressAsync(AnimeItem item, int progress, UserAnimeStatus? status = null);
    Task UpdateScoreAsync(AnimeItem item, string score);
    Task UpdateMetadataAsync(AnimeItem item);
    Task DeleteAsync(int id);

    /// <summary>
    /// Mirrors a remote tracker snapshot into the local table: upserts items
    /// that exist remotely, deletes items that don't. Refuses to delete a
    /// non-empty local list when the incoming list is empty (defensive against
    /// transient API failures returning an empty body).
    /// </summary>
    Task SyncFromRemoteAsync(IEnumerable<AnimeItem> items, MediaKind[]? syncKinds = null);

    /// <summary>Local poster paths for items currently tracked. Used by image cache cleanup.</summary>
    Task<List<string>> GetActiveLocalImagePathsAsync();
}
