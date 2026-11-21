using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

public interface IProgressChangeRepository
{
    /// <summary>Saves progress, history and tracker tasks together. An advance-only change can be skipped if already recorded.</summary>
    Task<bool> SaveAsync(
        AnimeEntity item, UserAnimeStatus? nextStatus, HistoryItem history, IReadOnlyList<SyncTaskEntity> tasks,
        bool onlyAdvance, CancellationToken ct);

    /// <summary>An absent item deletes the library entry for the supplied key.</summary>
    Task SaveEntryAsync(int id, MediaKind kind, AnimeEntity? item, IReadOnlyList<HistoryItem> history,
        IReadOnlyList<SyncTaskEntity> tasks, CancellationToken ct);
}
