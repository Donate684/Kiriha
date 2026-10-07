using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Services;

public interface IHistoryService
{
    /// <summary>Fired (on the calling thread) whenever a new history entry is committed to the DB.</summary>
    event Action<HistoryItem>? EntryAdded;
    event Action<int, int?, string, TrackerSyncState, string?>? TrackerStatusUpdated;

    /// <summary>Awaitable overload – guarantees the DB row exists before returning.</summary>
    Task AddEntryAsync(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null, CancellationToken ct = default);
    Task AddEntryAsync(int animeId, string title, string? russianTitle, int episode, string actionType, object? detail, string? posterUrl, CancellationToken ct = default);
    void AddEntry(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null);
    void AddEntry(int animeId, string title, string? russianTitle, int episode, string actionType, object? detail, string? posterUrl);

    void UpdateTrackerStatus(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error = null);
    void SetPendingTrackers(int animeId, int? episode, IEnumerable<string> trackerNames);
    Task UpdateTrackerStatusAsync(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error = null, CancellationToken ct = default);
    Task SetPendingTrackersAsync(int animeId, int? episode, IEnumerable<string> trackerNames, CancellationToken ct = default);

    Task<List<HistoryItem>> GetHistoryAsync(int limit = 1000, CancellationToken ct = default);
    Task FlushAsync(TimeSpan? timeout = null);
}

