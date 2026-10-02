using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;
using Moq;

namespace Kiriha.Tests;

public class SyncManagerNetworkFailureTests
{
    private readonly Mock<ITrackerService> _mockTracker;
    private readonly Mock<ISyncTaskRepository> _mockSyncTaskRepo;
    private readonly Mock<IDatabaseInitializer> _mockDbInit;
    private readonly Mock<IHistoryService> _mockHistoryService;
    private readonly Mock<IBackgroundTaskSupervisor> _mockBackgroundTasks;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly SyncManager _syncManager;

    public SyncManagerNetworkFailureTests()
    {
        _mockTracker = new Mock<ITrackerService>();
        _mockTracker.Setup(t => t.Name).Returns("Shikimori");
        _mockTracker.Setup(t => t.IsEnabled).Returns(true);

        _mockSyncTaskRepo = new Mock<ISyncTaskRepository>();
        _mockSyncTaskRepo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncTaskEntity>());

        _mockDbInit = new Mock<IDatabaseInitializer>();
        _mockDbInit.Setup(d => d.InitializationTask).Returns(Task.CompletedTask);

        _mockHistoryService = new Mock<IHistoryService>();
        _mockBackgroundTasks = new Mock<IBackgroundTaskSupervisor>();
        _mockNotificationService = new Mock<INotificationService>();

        _syncManager = new SyncManager(
            new[] { _mockTracker.Object },
            _mockSyncTaskRepo.Object,
            _mockDbInit.Object,
            _mockHistoryService.Object,
            _mockBackgroundTasks.Object,
            _mockNotificationService.Object
        );
    }

    [Fact]
    public async Task TransientNetworkError_SetsStatusToRetrying_AndNotifiesUser()
    {
        // Arrange: tracker throws or returns TransientFailure
        _mockTracker
            .Setup(t => t.UpdateProgressAsync(100, 5, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.TransientFailure);

        _mockSyncTaskRepo
            .Setup(r => r.AddAsync(It.IsAny<SyncTaskEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act: Enqueue an update
        await _syncManager.EnqueueUpdateAsync(100, 5);

        // Assert pending was marked initially
        _mockHistoryService.Verify(h => h.SetPendingTrackers(100, 5, It.Is<IEnumerable<string>>(names => names.Contains("Shikimori"))), Times.Once);

        // Process queue directly using private method ExecuteTaskAsync
        var executeMethod = typeof(SyncManager).GetMethod("ExecuteTaskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        var task = new Kiriha.Core.Tracking.Sync.Models.SyncTask
        {
            Id = 1,
            AnimeId = 100,
            Progress = 5,
            Type = Kiriha.Core.Tracking.Sync.Models.SyncTaskType.UpdateProgress
        };

        var taskResult = (Task<(bool Success, bool Executed)>)executeMethod.Invoke(_syncManager, new object[] { task, CancellationToken.None })!;
        var (success, didExecute) = await taskResult;

        // Assert: not successful, but executed
        Assert.False(success);
        Assert.True(didExecute);

        // Verify history updated with Retrying status
        _mockHistoryService.Verify(h => h.UpdateTrackerStatus(100, 5, "Shikimori", TrackerSyncState.Retrying, It.IsAny<string?>()), Times.Once);

        // Verify early failure notification was sent with willRetry = true
        _mockNotificationService.Verify(n => n.NotifySyncFailed(It.IsAny<string>(), "Shikimori", It.IsAny<string>(), true), Times.Once);
    }

    [Fact]
    public async Task PermanentAuthError_SetsStatusToFailed_AndNotifiesUserWithoutRetry()
    {
        // Arrange: 401 Unauthorized / PermanentFailure
        _mockTracker
            .Setup(t => t.UpdateProgressAsync(200, 3, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.PermanentFailure);

        var executeMethod = typeof(SyncManager).GetMethod("ExecuteTaskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        var task = new Kiriha.Core.Tracking.Sync.Models.SyncTask
        {
            Id = 2,
            AnimeId = 200,
            Progress = 3,
            Type = Kiriha.Core.Tracking.Sync.Models.SyncTaskType.UpdateProgress
        };

        var taskResult = (Task<(bool Success, bool Executed)>)executeMethod.Invoke(_syncManager, new object[] { task, CancellationToken.None })!;
        var (success, didExecute) = await taskResult;

        // PermanentFailure is resolved so it counts as success (not retried)
        Assert.True(success);
        Assert.True(didExecute);

        // Verify history marked as Failed
        _mockHistoryService.Verify(h => h.UpdateTrackerStatus(200, 3, "Shikimori", TrackerSyncState.Failed, It.IsAny<string?>()), Times.Once);

        // Verify notification sent with willRetry = false
        _mockNotificationService.Verify(n => n.NotifySyncFailed(It.IsAny<string>(), "Shikimori", It.IsAny<string>(), false), Times.Once);
    }

    [Fact]
    public async Task InternalProgramException_NeverAssumesSuccess_MarksRetryingAndNotifies()
    {
        // Arrange: tracker throws an unexpected internal exception (e.g. JsonException or NullReference)
        _mockTracker
            .Setup(t => t.UpdateProgressAsync(300, 1, null, null, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Internal serialization or network glitch"));

        var executeMethod = typeof(SyncManager).GetMethod("ExecuteTaskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        var task = new Kiriha.Core.Tracking.Sync.Models.SyncTask
        {
            Id = 3,
            AnimeId = 300,
            Progress = 1,
            Type = Kiriha.Core.Tracking.Sync.Models.SyncTaskType.UpdateProgress
        };

        var taskResult = (Task<(bool Success, bool Executed)>)executeMethod.Invoke(_syncManager, new object[] { task, CancellationToken.None })!;
        var (success, didExecute) = await taskResult;

        // Assert: MUST NOT be counted as successful
        Assert.False(success);
        Assert.True(didExecute);

        // Tracker must NOT be in successful list
        Assert.DoesNotContain("Shikimori", task.SuccessfulTrackers);

        // Verify history was marked as Retrying (NOT Success)
        _mockHistoryService.Verify(h => h.UpdateTrackerStatus(300, 1, "Shikimori", TrackerSyncState.Retrying, "Internal serialization or network glitch"), Times.Once);

        // Verify notification sent
        _mockNotificationService.Verify(n => n.NotifySyncFailed(It.IsAny<string>(), "Shikimori", "Internal serialization or network glitch", true), Times.Once);
    }
}

