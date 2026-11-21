using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Models;

public enum TrackerSyncState
{
    NotSynced,
    Pending,
    Success,
    Retrying,
    Failed,
    Superseded
}

public class TrackerSyncInfo
{
    public TrackerSyncState State { get; set; } = TrackerSyncState.NotSynced;
    public string? ErrorMessage { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public SyncTaskEntity? OriginalTask { get; set; }
}

