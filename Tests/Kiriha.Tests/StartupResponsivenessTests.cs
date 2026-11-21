using System.Collections.Specialized;
using Avalonia.Threading;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Library;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Kiriha.ViewModels.Startup;
using Moq;

namespace Kiriha.Tests;

public sealed class StartupResponsivenessTests
{
    [Fact]
    public async Task Startup_DispatcherHandlesInputWhileDatabaseAndLibraryPerformSynchronousWork()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stopDispatcher = new CancellationTokenSource();
        using var finishDatabase = new ManualResetEventSlim();
        using var finishLibrary = new ManualResetEventSlim();
        var dispatcherStarted = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcherStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = new Thread(() =>
        {
            try
            {
                var current = Dispatcher.CurrentDispatcher;
                AvaloniaSynchronizationContext.InstallIfNeeded();
                dispatcherStarted.SetResult(current);
                current.MainLoop(stopDispatcher.Token);
                dispatcherStopped.SetResult();
            }
            catch (Exception ex)
            {
                dispatcherStarted.TrySetException(ex);
                dispatcherStopped.TrySetException(ex);
            }
        }) { IsBackground = true };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        try
        {
            var dispatcher = await dispatcherStarted.Task.WaitAsync(timeout.Token);
            var ui = new TestUiDispatcher(dispatcher);
            var databaseEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var libraryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var database = new Mock<IDatabaseInitializer>();
            database.SetupGet(x => x.InitializationTask).Returns(Task.CompletedTask);
            database.Setup(x => x.InitializeAsync()).Returns(() =>
            {
                databaseEntered.SetResult(dispatcher.CheckAccess());
                finishDatabase.Wait(timeout.Token);
                return Task.CompletedTask;
            });
            var items = Enumerable.Range(1, 2500)
                .Select(id => new AnimeEntity { Id = id, Title = $"Title {id}", Status = UserAnimeStatus.Watching })
                .ToList();
            var store = new Mock<IUserAnimeRepository>();
            store.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                libraryEntered.SetResult(dispatcher.CheckAccess());
                finishLibrary.Wait(timeout.Token);
                return Task.FromResult(items);
            });
            using var background = new BackgroundTaskSupervisor();
            using var maintenance = new MaintenanceService([], background);
            var library = new AnimeRepository(store.Object, database.Object, background, ui, Mock.Of<IRecognitionCache>());
            int resets = 0;
            library.Collection.CollectionChanged += (_, e) =>
            {
                Assert.True(dispatcher.CheckAccess());
                Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action);
                Assert.Same(items[^1], library.FindById(items[^1].Id));
                resets++;
            };
            var settings = new Mock<ISettingsService>();
            var state = new AppSettings();
            state.System.KeepPlayerProcessAlive = false;
            settings.SetupGet(x => x.Current).Returns(state);
            var readiness = new AppReadinessService(database.Object, library, Mock.Of<IAnimeRefreshService>(),
                Mock.Of<IDiscordService>(), maintenance, settings.Object, Mock.Of<ISeasonalHiddenRepository>(),
                Mock.Of<ITorrentFilterRepository>(), [], background);
            using var welcome = await dispatcher.InvokeAsync(() => new WelcomeViewModel(readiness, settings.Object, ui),
                DispatcherPriority.Normal, timeout.Token);
            var startup = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.Post(() => startup.SetResult(readiness.StartAsync()), DispatcherPriority.Background);

            try
            {
                Assert.False(await databaseEntered.Task.WaitAsync(timeout.Token));
                var running = await startup.Task.WaitAsync(timeout.Token);
                await dispatcher.InvokeAsync(() =>
                {
                    Assert.True(welcome.IsLoading);
                    Assert.False(readiness.ReadyTask.IsCompleted);
                    Assert.Same(running, readiness.StartAsync());
                }, DispatcherPriority.Input, timeout.Token);
                finishDatabase.Set();

                Assert.False(await libraryEntered.Task.WaitAsync(timeout.Token));
                await dispatcher.InvokeAsync(() =>
                {
                    Assert.True(welcome.IsLoading);
                    Assert.Empty(library.Collection);
                    Assert.False(readiness.ReadyTask.IsCompleted);
                }, DispatcherPriority.Input, timeout.Token);
                finishLibrary.Set();

                await running.WaitAsync(timeout.Token);
                await readiness.ReadyTask.WaitAsync(timeout.Token);
                await dispatcher.InvokeAsync(() =>
                {
                    Assert.Equal(AppReadinessState.Ready, readiness.State);
                    Assert.False(welcome.IsLoading);
                    Assert.Equal(items.Count, library.Collection.Count);
                    Assert.Equal(1, resets);
                }, DispatcherPriority.Background, timeout.Token);
                database.Verify(x => x.InitializeAsync(), Times.Once);
            }
            finally
            {
                finishDatabase.Set();
                finishLibrary.Set();
                if (startup.Task.IsCompletedSuccessfully)
                    await (await startup.Task).WaitAsync(timeout.Token);
            }
        }
        finally
        {
            await stopDispatcher.CancelAsync();
            await dispatcherStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class TestUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
    {
        public bool HasThreadAffinity => true;
        public bool CheckAccess() => dispatcher.CheckAccess();
        public void Post(Action action) => dispatcher.Post(action);
        public async Task InvokeAsync(Action action) => await dispatcher.InvokeAsync(action);
        public async Task<T> InvokeAsync<T>(Func<T> action) => await dispatcher.InvokeAsync(action);
        public Task InvokeAsync(Func<Task> action) => dispatcher.InvokeAsync(action);
    }
}
