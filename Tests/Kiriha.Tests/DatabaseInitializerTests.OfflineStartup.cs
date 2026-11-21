using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Library;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Kiriha.Infrastructure.Settings;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class DatabaseInitializerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DesktopStartup_WithUnavailableTracker_IsReadyAndPreservesPendingQueue(bool hasCachedLibrary)
    {
        var directory = Directory.CreateTempSubdirectory("Kiriha.OfflineStartup.");
        try
        {
            var factory = new TestDbContextFactory(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory.FullName, "library.db"),
                    Pooling = false
                }.ToString())
                .Options);
            var initializer = new DatabaseInitializer(factory);
            await initializer.InitializeAsync();
            if (hasCachedLibrary)
                await SeedLibraryAsync(factory);

            var queue = new SyncTaskRepository(factory);
            var repository = new UserAnimeRepository(factory);
            var history = new HistoryService(new HistoryRepository(factory));
            var events = new CoreEventBus();
            var recognition = Mock.Of<IRecognitionCache>();
            var tracker = new Mock<ITrackerService>();
            tracker.SetupGet(t => t.IsEnabled).Returns(true);
            tracker.SetupGet(t => t.Name).Returns("MyAnimeList");
            tracker.Setup(t => t.UpdateProgressAsync(
                    It.IsAny<int>(), 8, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Simulated loss of network"));

            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishRequest = new TaskCompletionSource<List<AnimeEntity>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            tracker.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) =>
                {
                    requestStarted.SetResult();
                    return finishRequest.Task.WaitAsync(ct);
                });

            using var background = new BackgroundTaskSupervisor();
            using var maintenance = new MaintenanceService([], background);
            var library = new AnimeRepository(repository, initializer, background, new StartupDispatcher(), recognition);
            var orchestrator = new AnimeSyncOrchestrator(library, repository, [tracker.Object], recognition,
                syncTaskRepo: queue, eventBus: events);
            var refresh = new AnimeRefreshService(orchestrator, Mock.Of<IAiringInfoService>());
            var sync = new SyncManager([tracker.Object], queue, initializer, history, background);
            var settings = new SettingsService(Path.Combine(directory.FullName, "settings.json"));
            settings.Update(s => s.System.KeepPlayerProcessAlive = false, save: false);
            var readiness = new AppReadinessService(initializer, library, refresh, Mock.Of<IDiscordService>(),
                maintenance, settings, Mock.Of<ISeasonalHiddenRepository>(), Mock.Of<ITorrentFilterRepository>(),
                [sync], background);

            var startup = readiness.StartAsync();
            using var timeout = new CancellationTokenSource(MigrationTimeout);
            try
            {
                if (!hasCachedLibrary)
                    await requestStarted.Task.WaitAsync(timeout.Token);
                await readiness.ReadyTask.WaitAsync(timeout.Token);
                Assert.Equal(AppReadinessState.Ready, readiness.State);

                if (hasCachedLibrary)
                {
                    var cached = Assert.Single(library.Collection);
                    Assert.Equal(8, cached.Progress);
                    while ((await queue.GetPendingAsync(timeout.Token)).Single().RetryCount == 1)
                        await Task.Delay(20, timeout.Token);
                    var pending = Assert.Single(await queue.GetPendingAsync(timeout.Token));
                    Assert.Equal(cached.Id, pending.AnimeId);
                    Assert.Equal(8, pending.Progress);
                    Assert.Equal(2, pending.RetryCount);
                    tracker.Verify(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()), Times.Never);
                }
                else
                {
                    Assert.Empty(library.Collection);
                    Assert.False(finishRequest.Task.IsCompleted);
                }
            }
            finally
            {
                finishRequest.TrySetResult(null);
                await startup.WaitAsync(MigrationTimeout);
                await sync.StopAsync(CancellationToken.None);
                await background.StopAsync(timeout.Token);
                await history.FlushAsync(MigrationTimeout);
            }

            if (hasCachedLibrary)
            {
                var pending = Assert.Single(await queue.GetPendingAsync());
                await ReplayPendingTaskAsync(initializer, queue, history, pending.AnimeId);
                Assert.Empty(await queue.GetPendingAsync());
                Assert.Equal(8, Assert.Single(await repository.GetAllAsync()).Progress);
            }
        }
        finally
        {
            await DeleteMigrationDirectoryAsync(directory);
        }
    }

    private sealed class StartupDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
    }
}
