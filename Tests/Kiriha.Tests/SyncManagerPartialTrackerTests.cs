using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Core.Tracking.Sync.Models;
using Moq;

namespace Kiriha.Tests;

public class SyncManagerPartialTrackerTests
{
    private readonly Mock<ITrackerService> _mockShiki;
    private readonly Mock<ITrackerService> _mockMal;
    private readonly Mock<ISyncTaskRepository> _mockSyncTaskRepo;
    private readonly Mock<IDatabaseInitializer> _mockDbInit;
    private readonly Mock<IHistoryService> _mockHistoryService;
    private readonly Mock<IBackgroundTaskSupervisor> _mockBackgroundTasks;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly SyncManager _syncManager;

    public SyncManagerPartialTrackerTests()
    {
        _mockShiki = new Mock<ITrackerService>();
        _mockShiki.Setup(t => t.Name).Returns("Shikimori");
        _mockShiki.Setup(t => t.IsEnabled).Returns(true);

        _mockMal = new Mock<ITrackerService>();
        _mockMal.Setup(t => t.Name).Returns("MyAnimeList");
        _mockMal.Setup(t => t.IsEnabled).Returns(true);

        _mockSyncTaskRepo = new Mock<ISyncTaskRepository>();
        _mockDbInit = new Mock<IDatabaseInitializer>();
        _mockDbInit.Setup(d => d.InitializationTask).Returns(Task.CompletedTask);

        _mockHistoryService = new Mock<IHistoryService>();
        _mockBackgroundTasks = new Mock<IBackgroundTaskSupervisor>();
        _mockNotificationService = new Mock<INotificationService>();

        _syncManager = new SyncManager(
            new[] { _mockShiki.Object, _mockMal.Object },
            _mockSyncTaskRepo.Object,
            _mockDbInit.Object,
            _mockHistoryService.Object,
            _mockBackgroundTasks.Object,
            _mockNotificationService.Object
        );
    }

    [Fact]
    public async Task MultiTracker_ShikiSucceeds_MalFails_UpdatesRespectiveStatuses()
    {
        // Arrange
        // Shikimori returns 200 OK (Success)
        _mockShiki
            .Setup(t => t.UpdateProgressAsync(500, 10, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        // MyAnimeList returns 500 error / TransientFailure
        _mockMal
            .Setup(t => t.UpdateProgressAsync(500, 10, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.TransientFailure);

        var task = new SyncTask
        {
            Id = 5,
            AnimeId = 500,
            Progress = 10,
            Type = SyncTaskType.UpdateProgress
        };

        var executeMethod = typeof(SyncManager).GetMethod("ExecuteTaskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        // Act 1: First attempt
        var taskResult = (Task<(bool Success, bool Executed)>)executeMethod.Invoke(_syncManager, new object[] { task, CancellationToken.None })!;
        var (success, didExecute) = await taskResult;

        // Assert 1: Overall not completely successful because MAL failed
        Assert.False(success);
        Assert.True(didExecute);

        // Verify Shikimori is marked as Success
        _mockHistoryService.Verify(h => h.UpdateTrackerStatus(500, 10, "Shikimori", TrackerSyncState.Success, It.IsAny<string?>()), Times.Once);
        Assert.Contains("Shikimori", task.SuccessfulTrackers);

        // Verify MAL is marked as Retrying
        _mockHistoryService.Verify(h => h.UpdateTrackerStatus(500, 10, "MyAnimeList", TrackerSyncState.Retrying, It.IsAny<string?>()), Times.Once);
        Assert.DoesNotContain("MyAnimeList", task.SuccessfulTrackers);

        // Verify early notification only sent for MAL
        _mockNotificationService.Verify(n => n.NotifySyncFailed(It.IsAny<string>(), "MyAnimeList", It.IsAny<string>(), true), Times.Once);
        _mockNotificationService.Verify(n => n.NotifySyncFailed(It.IsAny<string>(), "Shikimori", It.IsAny<string>(), It.IsAny<bool>()), Times.Never);

        // Reset invocations to test retry behavior
        _mockShiki.Invocations.Clear();
        _mockMal.Invocations.Clear();

        // Arrange for retry: MAL now succeeds
        _mockMal
            .Setup(t => t.UpdateProgressAsync(500, 10, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        // Act 2: Second attempt (Retry)
        task.RetryCount = 1;
        var retryResult = (Task<(bool Success, bool Executed)>)executeMethod.Invoke(_syncManager, new object[] { task, CancellationToken.None })!;
        var (retrySuccess, retryDidExecute) = await retryResult;

        // Assert 2:
        Assert.True(retrySuccess);
        Assert.True(retryDidExecute);

        // Shikimori was already successful so it MUST NOT be called again
        _mockShiki.Verify(t => t.UpdateProgressAsync(It.IsAny<int>(), It.IsAny<int>(), null, null, null, null, It.IsAny<CancellationToken>()), Times.Never);

        // MAL was called and now succeeded
        _mockMal.Verify(t => t.UpdateProgressAsync(500, 10, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
        _mockHistoryService.Verify(h => h.UpdateTrackerStatus(500, 10, "MyAnimeList", TrackerSyncState.Success, It.IsAny<string?>()), Times.Once);
        Assert.Contains("MyAnimeList", task.SuccessfulTrackers);
    }
}
