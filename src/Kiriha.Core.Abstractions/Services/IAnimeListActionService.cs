using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public record ListActionResult(
    bool Success,
    bool HasActiveTrackers,
    string? Message = null)
{
    public static ListActionResult Ok(bool hasActiveTrackers = true, string? message = null)
        => new(true, hasActiveTrackers, message);

    public static ListActionResult Fail(string message)
        => new(false, false, message);
}

public interface IAnimeListActionService
{
    Task<ListActionResult> AddToListAsync(
        AnimeEntity item,
        UserAnimeStatus status,
        int progress = 0,
        CancellationToken ct = default);

    Task<ListActionResult> SaveAnimeAsync(
        AnimeEntity originalItem,
        AnimeEntity updatedItem,
        CancellationToken ct = default);

    Task<ListActionResult> RemoveFromListAsync(
        int animeId,
        CancellationToken ct = default);
}
