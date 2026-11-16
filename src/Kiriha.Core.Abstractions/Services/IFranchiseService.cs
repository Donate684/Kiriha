using Kiriha.Core.Domain.Models.Api;
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
    /// Enqueues an anime to resolve its franchise in background (e.g. from viewport).
    /// </summary>
    void EnqueueForResolution(AnimeEntity item);

    /// <summary>
    /// Ingests a full Shikimori franchise graph response and updates the in-memory franchise index.
    /// </summary>
    Task IngestFranchiseAsync(int sourceMalId, ShikiFranchiseResponse data, CancellationToken ct = default);
}
