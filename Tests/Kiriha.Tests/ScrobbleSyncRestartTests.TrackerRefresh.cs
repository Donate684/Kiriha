using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Library;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Repository;
using Kiriha.Services.AppLifecycle;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Theory]
    [InlineData(MediaKind.Anime, 7, 8, false)]
    [InlineData(MediaKind.Anime, 9, 8, false)]
    [InlineData(MediaKind.Anime, 7, 8, true)]
    [InlineData(MediaKind.Manga, 9, 8, false)]
    public async Task TrackerRefresh_StaleListAfterConfirmedEdit_PreservesLocalProgress(
        MediaKind kind, int previousProgress, int updatedProgress, bool editBeforeFetch)
    {
        var factory = CreateFactory();
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var savedLibrary = new UserAnimeRepository(factory);
        await savedLibrary.UpsertAsync(CreateItem(AnimeId, "Hagane no Renkinjutsushi", previousProgress));
        const int otherAnimeId = AnimeId + 1;
        await savedLibrary.UpsertAsync(CreateItem(otherAnimeId, "Sousou no Frieren", 1));

        var queue = new SyncTaskRepository(factory);
        var history = new HistoryService(new HistoryRepository(factory));
        var tracker = CreateTracker(Shikimori, _ => Task.FromResult(SyncOutcome.Success));
        tracker.Setup(t => t.SaveFullListStatusAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);
        var listRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<List<AnimeEntity>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>())).Returns(FetchAsync);
        tracker.Setup(t => t.GetUserMangaListAsync(It.IsAny<CancellationToken>())).Returns(FetchAsync);

        var dispatcher = new ImmediateProgressDispatcher();
        var initializer = new Mock<IDatabaseInitializer>();
        initializer.SetupGet(db => db.InitializationTask).Returns(Task.CompletedTask);
        var recognition = Mock.Of<IRecognitionCache>();
        using var background = new BackgroundTaskSupervisor();
        using var gate = new LibrarySyncGate();
        var library = new AnimeRepository(savedLibrary, initializer.Object, background, dispatcher, recognition);
        await library.InitializeAsync();
        var item = library.FindById(AnimeId, kind);
        Assert.NotNull(item);
        var remoteItem = item.Clone();
        remoteItem.Synopsis = "Synopsis from the tracker";
        var remoteOther = library.FindById(otherAnimeId, kind)?.Clone();
        Assert.NotNull(remoteOther);
        SetRemoteProgress(remoteOther, 2);

        var events = new CoreEventBus();
        var sync = CreateSyncManager(factory, queue, history, background, tracker.Object);
        using var progress = new AnimeProgressService(library, savedLibrary, sync, history, dispatcher, eventBus: events, syncGate: gate);
        var refresh = new AnimeSyncOrchestrator(
            library, savedLibrary, [tracker.Object], recognition, syncGate: gate, syncTaskRepo: queue, eventBus: events);
        using var timeout = new CancellationTokenSource(Timeout);
        Task<bool>? refreshTask = null;
        try
        {
            if (editBeforeFetch)
                await EditAsync();
            refreshTask = RefreshAsync();
            await listRequested.Task.WaitAsync(timeout.Token);
            if (!editBeforeFetch)
                await EditAsync();
            await DrainRestoredQueueAsync(factory, queue, history, tracker.Object);

            if (kind == MediaKind.Anime)
            {
                tracker.Verify(t => t.UpdateProgressAsync(
                    AnimeId, updatedProgress, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
            }
            else
            {
                tracker.Verify(t => t.SaveFullListStatusAsync(
                    It.Is<AnimeEntity>(saved => saved.Id == AnimeId && saved.ChaptersRead == updatedProgress),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
            Assert.Empty(await queue.GetPendingAsync(timeout.Token));
            Assert.Equal(updatedProgress, ReadProgress(item));

            response.SetResult([remoteItem, remoteOther]);
            Assert.True(await refreshTask.WaitAsync(timeout.Token));
            Assert.Equal(updatedProgress, ReadProgress(item));
            Assert.Equal(remoteItem.Synopsis, item.Synopsis);
            var otherItem = library.FindById(otherAnimeId, kind);
            Assert.NotNull(otherItem);
            Assert.Equal(2, ReadProgress(otherItem));
            var reopenedLibrary = new UserAnimeRepository(CreateFactory());
            await AssertSavedProgressAsync(reopenedLibrary, AnimeId, updatedProgress);
            await AssertSavedProgressAsync(reopenedLibrary, otherAnimeId, 2);

            // A separate request can import a subsequent edit made on the tracker itself.
            int laterRemoteProgress = updatedProgress + 1;
            var laterRemoteItem = remoteItem.Clone();
            SetRemoteProgress(laterRemoteItem, laterRemoteProgress);
            tracker.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([laterRemoteItem, remoteOther.Clone()]);
            tracker.Setup(t => t.GetUserMangaListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([laterRemoteItem, remoteOther.Clone()]);
            Assert.True(await RefreshAsync());
            Assert.Equal(laterRemoteProgress, ReadProgress(item));
            await AssertSavedProgressAsync(reopenedLibrary, AnimeId, laterRemoteProgress);
        }
        finally
        {
            await timeout.CancelAsync();
            if (refreshTask is { IsCompleted: false })
                await ((Task)refreshTask).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            await sync.StopAsync(CancellationToken.None);
        }

        Task EditAsync() => previousProgress < updatedProgress
            ? progress.RecordScrobbleAsync(item, updatedProgress, null, timeout.Token)
            : progress.SmartDecrementProgressAsync(item);

        Task<bool> RefreshAsync() => kind == MediaKind.Anime
            ? refresh.SyncWithTrackersAsync(ct: timeout.Token)
            : refresh.SyncMangaWithTrackersAsync(ct: timeout.Token);

        Task<List<AnimeEntity>?> FetchAsync(CancellationToken ct)
        {
            listRequested.SetResult();
            return response.Task.WaitAsync(ct);
        }

        AnimeEntity CreateItem(int id, string title, int value)
        {
            var entity = new AnimeEntity { Id = id, MediaKind = kind, Title = title, Status = UserAnimeStatus.Watching };
            SetRemoteProgress(entity, value);
            return entity;
        }

        static void SetRemoteProgress(AnimeEntity entity, int value)
        {
            if (entity.MediaKind == MediaKind.Anime)
                entity.Progress = value;
            else
                entity.ChaptersRead = value;
        }

        async Task AssertSavedProgressAsync(UserAnimeRepository repository, int id, int expected)
        {
            var persisted = await repository.GetByIdAsync(id, kind, timeout.Token);
            Assert.NotNull(persisted);
            Assert.Equal(expected, ReadProgress(persisted));
        }
    }
}
