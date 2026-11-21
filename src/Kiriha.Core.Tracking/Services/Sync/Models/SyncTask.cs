using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Sync.Models;

public enum SyncTaskType
{
    UpdateProgress,
    FullUpdate,
    Remove
}

public class SyncTask
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public MediaKind MediaKind { get; set; } = MediaKind.Anime;
    public SyncTaskType Type { get; set; }
    public int? Progress { get; set; }
    public UserAnimeStatus? Status { get; set; }
    public int? Score { get; set; }
    public AnimeEntity? FullItem { get; set; }
    public int RetryCount { get; set; } = 0;
    public string? TargetTracker { get; set; }
    internal SyncTaskOwner? Owner { get; set; }
    internal bool IsDirect { get; set; }
    internal int? HistoryId { get; set; }
    public HashSet<string> SuccessfulTrackers { get; set; } = new();
}
