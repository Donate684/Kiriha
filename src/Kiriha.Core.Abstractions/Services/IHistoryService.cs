using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IHistoryService
{
    /// <summary>Fired (on the calling thread) whenever a new history entry is committed to the DB.</summary>
    event Action<HistoryItem>? EntryAdded;
    event Action<int, int?, string, TrackerSyncState, string?>? TrackerStatusUpdated;

    /// <summary>Awaitable overload – guarantees the DB row exists before returning.</summary>
    Task AddEntryAsync(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null, CancellationToken ct = default);
    Task AddEntryAsync(int animeId, string title, string? russianTitle, int episode, string actionType, object? detail, string? posterUrl, CancellationToken ct = default);
    Task AddEntryAsync(int animeId, string title, string? russianTitle, int episode, HistoryActionType actionType, object? detail = null, string? posterUrl = null, CancellationToken ct = default)
        => AddEntryAsync(animeId, title, russianTitle, episode, actionType.ToActionString(), detail, posterUrl, ct);
    Task AddEntryAsync(int animeId, MediaKind mediaKind, string title, string? russianTitle, int episode, HistoryActionType actionType, object? detail = null, string? posterUrl = null, CancellationToken ct = default)
        => AddEntryAsync(animeId, title, russianTitle, episode, actionType.ToActionString(), detail, posterUrl, ct);

    void AddEntry(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null);
    void AddEntry(int animeId, string title, string? russianTitle, int episode, string actionType, object? detail, string? posterUrl);
    void AddEntry(int animeId, string title, string? russianTitle, int episode, HistoryActionType actionType, object? detail = null, string? posterUrl = null)
        => AddEntry(animeId, title, russianTitle, episode, actionType.ToActionString(), detail, posterUrl);
    void AddEntry(int animeId, MediaKind mediaKind, string title, string? russianTitle, int episode, HistoryActionType actionType, object? detail = null, string? posterUrl = null)
        => AddEntry(animeId, title, russianTitle, episode, actionType.ToActionString(), detail, posterUrl);

    void UpdateTrackerStatus(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error = null);
    void UpdateTrackerStatus(int animeId, MediaKind mediaKind, int? episode, string trackerName, TrackerSyncState state, string? error = null)
        => UpdateTrackerStatus(animeId, episode, trackerName, state, error);

    void SetPendingTrackers(int animeId, int? episode, IEnumerable<string> trackerNames);
    void SetPendingTrackers(int animeId, MediaKind mediaKind, int? episode, IEnumerable<string> trackerNames)
        => SetPendingTrackers(animeId, episode, trackerNames);

    Task UpdateTrackerStatusAsync(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error = null, CancellationToken ct = default);
    Task UpdateTrackerStatusAsync(int animeId, MediaKind mediaKind, int? episode, string trackerName, TrackerSyncState state, string? error = null, CancellationToken ct = default)
        => UpdateTrackerStatusAsync(animeId, episode, trackerName, state, error, ct);

    Task SetPendingTrackersAsync(int animeId, int? episode, IEnumerable<string> trackerNames, CancellationToken ct = default);
    Task SetPendingTrackersAsync(int animeId, MediaKind mediaKind, int? episode, IEnumerable<string> trackerNames, CancellationToken ct = default)
        => SetPendingTrackersAsync(animeId, episode, trackerNames, ct);

    Task<List<HistoryItem>> GetHistoryAsync(int limit = 1000, CancellationToken ct = default);
    Task FlushAsync(TimeSpan? timeout = null);
}
