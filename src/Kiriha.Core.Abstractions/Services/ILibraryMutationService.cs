using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

/// <summary>
/// Shared core primitives for mutating user library state (in-memory index, UI collection,
/// local SQLite persistence, status transition rules, date stamping, and domain event dispatch).
/// </summary>
public interface ILibraryMutationService
{
    Task<AnimeEntity?> FindExistingInMemoryAsync(int animeId, MediaKind kind);
    Task<AnimeEntity?> FindExistingAsync(int animeId, MediaKind kind, CancellationToken ct = default);
    UserAnimeStatus? ComputeSmartNextStatus(AnimeEntity item, int nextProgress);
    void StampStatusDates(AnimeEntity item, UserAnimeStatus? nextStatus, bool isStarting, AnimeEntity? existing = null);
    Task<bool> ApplyLocalProgressAsync(AnimeEntity item, int nextProgress, UserAnimeStatus? nextStatus = null, CancellationToken ct = default);
    Task EnqueueProgressSyncAsync(AnimeEntity item, int nextProgress, UserAnimeStatus? nextStatus = null);
}
