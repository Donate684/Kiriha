using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IFranchiseService
{
    /// <summary>
    /// Gets the computed franchise context for a specific anime ID.
    /// </summary>
    FranchiseContext? GetFranchiseContext(int animeId);

    /// <summary>
    /// Gets the current in-memory index of all known franchise relationships.
    /// </summary>
    IReadOnlyDictionary<int, FranchiseContext> GetIndex();

    /// <summary>
    /// Event raised whenever the in-memory franchise index is updated or rebuilt.
    /// </summary>
    event System.Action? IndexRebuilt;

    /// <summary>
    /// Rebuilds the in-memory franchise index using the user's library and cached relations.
    /// </summary>
    Task RebuildIndexAsync(CancellationToken ct = default);

    /// <summary>
    /// Enriches the given collection of AnimeEntity instances in-place with their franchise context.
    /// </summary>
    void Enrich(IEnumerable<AnimeEntity> items);

    /// <summary>
    /// Enqueues an anime to resolve its franchise in background (e.g. from viewport).
    /// </summary>
    void EnqueueForResolution(AnimeEntity item);

    /// <summary>
    /// Ingests relations for a source anime and updates the in-memory franchise index.
    /// </summary>
    Task IngestRelationsAsync(int sourceMalId, IEnumerable<AnimeRelation> relations, CancellationToken ct = default);
}
