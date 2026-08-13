using System.Collections.Generic;
using System.Linq;
using Kiriha.Core.Domain.Constants;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeSyncOrchestrator
{
    private static bool IsRemoteSnapshotSafe(List<AnimeEntity> currentItems, List<AnimeEntity> apiList)
    {
        static int CountStatus(List<AnimeEntity> items, UserAnimeStatus status)
        {
            return status == UserAnimeStatus.Watching
                ? items.Count(x => x.Status == UserAnimeStatus.Watching || x.IsRewatching)
                : items.Count(x => x.Status == status);
        }

        foreach (var trackedStatus in new[]
        {
            UserAnimeStatus.Watching,
            UserAnimeStatus.Completed,
            UserAnimeStatus.OnHold,
            UserAnimeStatus.Dropped,
            UserAnimeStatus.PlanToWatch
        })
        {
            var local = CountStatus(currentItems, trackedStatus);
            if (local < SyncSafetyConstants.MinimumStatusGuardCount) continue;

            var incoming = CountStatus(apiList, trackedStatus);
            var dropped = local - incoming;
            if (dropped < SyncSafetyConstants.MinimumStatusDropCount) continue;

            var incomingRatio = (double)incoming / local;
            if (incomingRatio < SyncSafetyConstants.MaximumAllowedStatusDropRatio)
            {
                Log.Warning(
                    "SyncWithTrackers: aborting because {Status} count dropped suspiciously from {Local} to {Incoming}. Likely a partial or stale tracker snapshot.",
                    trackedStatus,
                    local,
                    incoming);
                return false;
            }
        }

        return true;
    }
}
