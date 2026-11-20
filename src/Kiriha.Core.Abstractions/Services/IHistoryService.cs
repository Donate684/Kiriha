using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IHistoryService
{
    /// <summary>Fired (on the calling thread) whenever a new history entry is committed to the DB.</summary>
    event Action<HistoryItem>? EntryAdded;
    event Action<int, MediaKind, int?, string, TrackerSyncState, string?>? TrackerStatusUpdated;

    void PublishCommittedEntry(HistoryItem entry);

    /// <summary>Awaitable entry append – guarantees the DB row exists before returning.</summary>
    Task AddEntryAsync(
        int animeId,
        MediaKind mediaKind,
        string title,
        string? russianTitle,
        int episode,
        HistoryActionType actionType,
        object? detail = null,
        string? posterUrl = null,
        CancellationToken ct = default);

    void AddEntry(
        int animeId,
        MediaKind mediaKind,
        string title,
        string? russianTitle,
        int episode,
        HistoryActionType actionType,
        object? detail = null,
        string? posterUrl = null);

    void UpdateTrackerStatus(
        int animeId,
        MediaKind mediaKind,
        int? episode,
        string trackerName,
        TrackerSyncState state,
        string? error = null);

    Task UpdateTrackerStatusAsync(
        int animeId,
        MediaKind mediaKind,
        int? episode,
        string trackerName,
        TrackerSyncState state,
        string? error = null,
        CancellationToken ct = default);

    void SetPendingTrackers(
        int animeId,
        MediaKind mediaKind,
        int? episode,
        IEnumerable<string> trackerNames);

    Task SetPendingTrackersAsync(
        int animeId,
        MediaKind mediaKind,
        int? episode,
        IEnumerable<string> trackerNames,
        CancellationToken ct = default);

    Task<List<HistoryItem>> GetHistoryAsync(int limit = 1000, CancellationToken ct = default);
    Task FlushAsync(TimeSpan? timeout = null);
}
