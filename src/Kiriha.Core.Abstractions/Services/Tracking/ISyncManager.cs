using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface ISyncManager
{
    Task EnqueueUpdateAsync(int animeId, int progress, UserAnimeStatus? status = null, int? score = null, string? targetTracker = null);
    Task EnqueueUpdateAsync(int animeId, MediaKind mediaKind, int progress, UserAnimeStatus? status = null, int? score = null, string? targetTracker = null)
        => EnqueueUpdateAsync(animeId, progress, status, score, targetTracker);
    Task EnqueueRemoveAsync(int animeId);
    Task EnqueueRemoveAsync(int animeId, MediaKind mediaKind);
    Task EnqueueFullUpdateAsync(AnimeEntity item);
}
