using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Core.Tracking.Sync.Models;
using Kiriha.Services;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public class ScrobbleToTrackerIntegrationTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly SettingsService _settingsService;
    private readonly Mock<ITrackerService> _mockTracker;
    private readonly Mock<ISyncTaskRepository> _mockSyncTaskRepo;
    private readonly Mock<IDatabaseInitializer> _mockDbInit;
    private readonly Mock<IHistoryService> _mockHistoryService;
    private readonly Mock<IAnimeRepository> _mockAnimeRepo;
    private readonly Mock<IUserAnimeRepository> _mockUserAnimeRepo;
    private readonly Mock<IBackgroundTaskSupervisor> _mockBackgroundTasks;
    private readonly Mock<IUiDispatcher> _mockUiDispatcher;
    private readonly Mock<ILocalizer> _mockLocalizer;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly SyncManager _syncManager;
    private readonly AnimeProgressService _progressService;
    private readonly ScrobbleService _scrobbleService;

    public ScrobbleToTrackerIntegrationTests()
    {
        _tempSettingsPath = Path.GetTempFileName();
        _settingsService = new SettingsService(_tempSettingsPath);
        _settingsService.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.DelaySeconds = 0;
            s.System.Scrobbler.NotifyOnSkippedEpisode = false;
        }, save: false);

        _mockTracker = new Mock<ITrackerService>();
        _mockTracker.Setup(t => t.Name).Returns("Shikimori");
        _mockTracker.Setup(t => t.IsEnabled).Returns(true);

        _mockSyncTaskRepo = new Mock<ISyncTaskRepository>();
        _mockDbInit = new Mock<IDatabaseInitializer>();
        _mockDbInit.Setup(d => d.InitializationTask).Returns(Task.CompletedTask);

        _mockHistoryService = new Mock<IHistoryService>();
        _mockAnimeRepo = new Mock<IAnimeRepository>();
        _mockUserAnimeRepo = new Mock<IUserAnimeRepository>();
        _mockBackgroundTasks = new Mock<IBackgroundTaskSupervisor>();
        _mockUiDispatcher = new Mock<IUiDispatcher>();
        _mockLocalizer = new Mock<ILocalizer>();
        _mockNotificationService = new Mock<INotificationService>();

        _mockBackgroundTasks
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                task(ct).GetAwaiter().GetResult();
            });

        _syncManager = new SyncManager(
            new[] { _mockTracker.Object },
            _mockSyncTaskRepo.Object,
            _mockDbInit.Object,
            _mockHistoryService.Object,
            _mockBackgroundTasks.Object,
            _mockNotificationService.Object
        );

        _progressService = new AnimeProgressService(
            _mockAnimeRepo.Object,
            _mockUserAnimeRepo.Object,
            _syncManager,
            _mockHistoryService.Object,
            _mockUiDispatcher.Object
        );

        _scrobbleService = new ScrobbleService(
            _progressService,
            _mockHistoryService.Object,
            _settingsService,
            _mockNotificationService.Object,
            _mockBackgroundTasks.Object,
            _mockUiDispatcher.Object,
            _mockLocalizer.Object
        );
    }

    [Fact]
    public async Task Scrobble_EndToEnd_DispatchesToTrackerAndUpdatesStatus()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "8", IsPlaying = true };
        var anime = new AnimeEntity { Id = 777, Title = "Fullmetal Alchemist", Progress = 7, Status = UserAnimeStatus.Watching };

        _mockTracker
            .Setup(t => t.UpdateProgressAsync(777, 8, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        SyncTask? enqueuedTask = null;
        _mockSyncTaskRepo
            .Setup(r => r.AddAsync(It.IsAny<SyncTaskEntity>(), It.IsAny<CancellationToken>()))
            .Callback<SyncTaskEntity, CancellationToken>((entity, ct) =>
            {
                enqueuedTask = new SyncTask
                {
                    Id = 1,
                    AnimeId = entity.AnimeId,
                    Progress = entity.Progress,
                    Type = Enum.Parse<SyncTaskType>(entity.Type)
                };
            })
            .ReturnsAsync(1);

        // Act 1: Scrobble starts
        _scrobbleService.StartScrobble(media, anime);

        // Assert 1: Local repository was updated
        _mockUserAnimeRepo.Verify(r => r.UpdateProgressAsync(anime, 8, null), Times.Once);

        // Assert 2: SyncTask was enqueued and pending status marked
        _mockSyncTaskRepo.Verify(r => r.AddAsync(It.Is<SyncTaskEntity>(e => e.AnimeId == 777 && e.Progress == 8), It.IsAny<CancellationToken>()), Times.Once);
        _mockHistoryService.Verify(h => h.SetPendingTrackers(777, 8, It.Is<IEnumerable<string>>(t => t.Contains("Shikimori"))), Times.Once);

        // Act 2: SyncManager processes the enqueued task
        Assert.NotNull(enqueuedTask);
        var executeMethod = typeof(SyncManager).GetMethod("ExecuteTaskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        var taskResult = (Task<(bool Success, bool Executed)>)executeMethod.Invoke(_syncManager, new object[] { enqueuedTask, CancellationToken.None })!;
        var (success, didExecute) = await taskResult;

        // Assert 3: Tracker received real call and history received Success status
        Assert.True(success);
        Assert.True(didExecute);
        _mockTracker.Verify(t => t.UpdateProgressAsync(777, 8, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
        _mockHistoryService.Verify(h => h.UpdateTrackerStatus(777, 8, "Shikimori", TrackerSyncState.Success, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task HistoryViewModel_BadgeClick_EnqueuesAndUpdatesStatusRealtime()
    {
        var mockRepo = new Mock<IHistoryRepository>();
        var items = new List<HistoryItem>
        {
            new HistoryItem
            {
                Id = 1,
                AnimeId = 28155,
                AnimeTitle = "Yoru no Yatterman",
                Episode = 4,
                ActionType = 1,
                Timestamp = DateTime.UtcNow
            }
        };
        mockRepo.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var historyService = new Kiriha.Services.Data.Core.HistoryService(mockRepo.Object);

        var mockTracker = new Mock<ITrackerService>();
        mockTracker.Setup(t => t.Name).Returns("Shikimori");
        mockTracker.Setup(t => t.IsEnabled).Returns(true);
        mockTracker.Setup(t => t.UpdateProgressAsync(28155, 4, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        var syncTaskRepo = new Mock<ISyncTaskRepository>();
        syncTaskRepo.Setup(r => r.AddAsync(It.IsAny<SyncTaskEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var bgTasks = new Mock<IBackgroundTaskSupervisor>();
        bgTasks.Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                _ = Task.Run(() => task(ct));
            });

        var syncManager = new SyncManager(
            new[] { mockTracker.Object },
            syncTaskRepo.Object,
            _mockDbInit.Object,
            historyService,
            bgTasks.Object,
            _mockNotificationService.Object
        );

        // Start SyncManager
        await syncManager.StartAsync(CancellationToken.None);

        var mockUiDispatcher = new Mock<IUiDispatcher>();
        mockUiDispatcher.Setup(d => d.Post(It.IsAny<Action>())).Callback<Action>(a => a());

        var vm = new Kiriha.ViewModels.History.HistoryViewModel(
            historyService,
            null!,
            _mockAnimeRepo.Object,
            null!,
            null!,
            _mockLocalizer.Object,
            syncManager,
            new[] { mockTracker.Object },
            _mockNotificationService.Object,
            mockUiDispatcher.Object
        );

        // Populate items directly in _rawItems for test without dbInit
        var rawField = typeof(Kiriha.ViewModels.History.HistoryViewModel).GetField("_rawItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        rawField.SetValue(vm, items);
        var applyMethod = typeof(Kiriha.ViewModels.History.HistoryViewModel).GetMethod("ApplyFilters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        applyMethod.Invoke(vm, null);

        Assert.NotEmpty(vm.TimelineItems);
        var entryVm = vm.TimelineItems.OfType<Kiriha.ViewModels.History.HistoryEntryVm>().First();
        var badge = entryVm.TrackerBadges.First(b => b.TrackerName == "Shikimori");
        Assert.Equal(TrackerSyncState.NotSynced, badge.State);

        // Click badge to sync!
        await badge.SyncCommand.ExecuteAsync(null);

        // Wait a short moment for background task to process
        await Task.Delay(500);

        Assert.Equal(TrackerSyncState.Success, badge.State);
    }

    public void Dispose()
    {
        _scrobbleService.Dispose();
        _settingsService.Dispose();
        if (File.Exists(_tempSettingsPath)) try { File.Delete(_tempSettingsPath); } catch { }
    }
}
