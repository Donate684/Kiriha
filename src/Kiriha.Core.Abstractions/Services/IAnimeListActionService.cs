using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public enum AnimeEditConflict
{
    None,
    EntryChanged,
    FieldsChanged
}

public record ListActionResult(
    bool Success,
    bool HasActiveTrackers,
    string? Message = null)
{
    public AnimeEditConflict Conflict { get; init; }
    public (AnimeEditFields Fields, long Generation)? CommittedEdit { get; init; }

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

    Task<ListActionResult> SaveEditorChangesAsync(
        int id, MediaKind kind, long generation, AnimeEditFields baseline, AnimeEditFields proposed,
        CancellationToken ct = default);

    Task<ListActionResult> AddFromEditorAsync(
        AnimeEntity item, UserAnimeStatus status, long generation, CancellationToken ct = default);

    Task<ListActionResult> RemoveFromEditorAsync(
        int id, MediaKind kind, long generation, CancellationToken ct = default);

    Task<ListActionResult> RemoveFromListAsync(
        int animeId,
        CancellationToken ct = default);

    Task<ListActionResult> RemoveFromListAsync(
        int animeId,
        MediaKind mediaKind,
        CancellationToken ct = default);
}
