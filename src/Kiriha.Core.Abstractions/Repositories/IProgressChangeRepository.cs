using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

public interface IProgressChangeRepository
{
    /// <summary>Saves progress, history and the tracker task together. An advance-only change can be skipped if already recorded.</summary>
    Task<bool> SaveAsync(
        AnimeEntity item, UserAnimeStatus? nextStatus, HistoryItem history, SyncTaskEntity task, bool onlyAdvance, CancellationToken ct);
}
