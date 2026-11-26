using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.ViewModels.History;
using Moq;

namespace Kiriha.Tests;

public sealed class HistoryTrackerRetryTests
{
    [Theory]
    [InlineData(TrackerSyncState.Pending)]
    [InlineData(TrackerSyncState.Retrying)]
    [InlineData(TrackerSyncState.Success)]
    public async Task StatusBecomesFailed_EnablesRetryWithoutReloadingHistory(TrackerSyncState initialState)
    {
        var original = new SyncTaskEntity { AnimeId = 42 };
        var source = new HistoryItem { AnimeId = 42 };
        source.SetTrackerStatus(TrackerConstants.Names.ShikiOrig, initialState, originalTask: original);
        var entry = new HistoryEntryVm(Mock.Of<ILocalizer>())
        {
            AnimeId = 42,
            ActionType = (int)HistoryActionType.Scrobbled,
            Primary = source
        };
        List<string> requests = [];
        entry.LoadTrackerStatuses(source.TrackerStatuses,
            tracker =>
            {
                requests.Add(tracker);
                entry.UpdateTrackerStatus(tracker, TrackerSyncState.Pending, null);
                return Task.CompletedTask;
            },
            _ => true,
            [TrackerConstants.Names.ShikiOrig],
            status => status is { State: TrackerSyncState.Failed, OriginalTask: not null });

        var badge = Assert.Single(entry.TrackerBadges);
        Assert.False(badge.SyncCommand.CanExecute(null));

        entry.UpdateTrackerStatus(TrackerConstants.Names.ShikiOrig, TrackerSyncState.Failed, "Unavailable");

        Assert.Same(badge, Assert.Single(entry.TrackerBadges));
        Assert.True(badge.IsClickable);
        Assert.True(badge.SyncCommand.CanExecute(null));
        Assert.Equal("Hand", badge.CursorKind);
        await badge.SyncCommand.ExecuteAsync(null);
        Assert.Equal([TrackerConstants.Names.ShikiOrig], requests);
        Assert.Equal(TrackerSyncState.Pending, badge.State);
        Assert.False(badge.SyncCommand.CanExecute(null));

        // A second failure must allow another attempt on the same badge.
        entry.UpdateTrackerStatus(TrackerConstants.Names.ShikiOrig, TrackerSyncState.Failed, "Unavailable again");
        Assert.True(badge.SyncCommand.CanExecute(null));
        await badge.SyncCommand.ExecuteAsync(null);
        Assert.Equal(2, requests.Count);

        entry.UpdateTrackerStatus(TrackerConstants.Names.ShikiOrig, TrackerSyncState.Success, null);
        Assert.False(badge.IsClickable);
        Assert.False(badge.SyncCommand.CanExecute(null));
    }

    [Fact]
    public void FailedReceipt_RechecksOriginalAccountBeforeRetry()
    {
        bool originalAccountAvailable = false;
        var source = new HistoryItem { AnimeId = 42 };
        source.SetTrackerStatus(TrackerConstants.Names.ShikiOrig, TrackerSyncState.Failed,
            originalTask: new SyncTaskEntity { AnimeId = 42 });
        var entry = new HistoryEntryVm(Mock.Of<ILocalizer>())
        {
            AnimeId = 42,
            ActionType = (int)HistoryActionType.Scrobbled,
            Primary = source
        };
        entry.LoadTrackerStatuses(source.TrackerStatuses, _ => Task.CompletedTask, _ => true,
            canRetry: status => status is { State: TrackerSyncState.Failed, OriginalTask: not null }
                && originalAccountAvailable);
        var badge = Assert.Single(entry.TrackerBadges);
        Assert.False(badge.SyncCommand.CanExecute(null));

        originalAccountAvailable = true;
        Assert.True(badge.SyncCommand.CanExecute(null));

        originalAccountAvailable = false;
        Assert.False(badge.SyncCommand.CanExecute(null));
    }

    [Fact]
    public void FailedReceiptWithoutOriginalTask_RemainsUnavailable()
    {
        var entry = new HistoryEntryVm(Mock.Of<ILocalizer>()) { AnimeId = 42 };
        entry.LoadTrackerStatuses(new Dictionary<string, TrackerSyncInfo>
        {
            [TrackerConstants.Names.ShikiOrig] = new() { State = TrackerSyncState.Failed }
        }, _ => Task.CompletedTask, _ => true, [TrackerConstants.Names.ShikiOrig],
            status => status is { State: TrackerSyncState.Failed, OriginalTask: not null });

        Assert.False(Assert.Single(entry.TrackerBadges).SyncCommand.CanExecute(null));
    }
}
