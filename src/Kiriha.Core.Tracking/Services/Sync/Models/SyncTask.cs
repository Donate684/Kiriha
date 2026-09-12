using System.Collections.Generic;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync.Models;

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
    public SyncTaskType Type { get; set; }
    public int? Progress { get; set; }
    public UserAnimeStatus? Status { get; set; }
    public int? Score { get; set; }
    public AnimeEntity? FullItem { get; set; }
    public int RetryCount { get; set; } = 0;
    public HashSet<string> SuccessfulTrackers { get; set; } = new();
}
