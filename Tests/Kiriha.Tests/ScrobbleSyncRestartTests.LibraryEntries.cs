using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
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
    [InlineData(MediaKind.Anime)]
    [InlineData(MediaKind.Manga)]
    public async Task LibraryActions_WhenQueueWriteFails_RollBackAndReplayOnlyTheFinalEntry(MediaKind kind)
    {
        var factory = CreateFactory();
        await using (var setup = factory.CreateDbContext())
            await setup.Database.EnsureCreatedAsync();

        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        var savedLibrary = new UserAnimeRepository(factory);
        var siblingKind = kind == MediaKind.Anime ? MediaKind.Manga : MediaKind.Anime;
        await savedLibrary.UpsertAsync(new AnimeEntity
        {
            Id = AnimeId, Title = "Other media", MediaKind = siblingKind, Status = UserAnimeStatus.Completed
        }, ct);

        var queue = new SyncTaskRepository(factory);
        var history = new HistoryService(new HistoryRepository(factory));
        var dispatcher = new ImmediateProgressDispatcher();
        var initializer = new Mock<IDatabaseInitializer>();
        initializer.SetupGet(db => db.InitializationTask).Returns(Task.CompletedTask);
        var account = new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal, IsPrimary = true, AuthenticatedUserId = 777,
            Tokens = new MalTokens { AccessToken = "test-mal" }
        };
        var settingsSnapshot = new AppSettings
        {
            Api = new AppSettings.ApiConfig { Accounts = [account] }
        };
        var settings = Mock.Of<ISettingsService>(service => service.Current == settingsSnapshot);
        var tracker = new Mock<ITrackerService>(MockBehavior.Strict);
        tracker.SetupGet(t => t.TrackerId).Returns(account.TrackerId);
        tracker.SetupGet(t => t.Name).Returns("MyAnimeList");
        var trackers = new[] { tracker.Object };
        using var background = new BackgroundTaskSupervisor();
        using var gate = new LibrarySyncGate();
        var library = new AnimeRepository(savedLibrary, initializer.Object, background, dispatcher, Mock.Of<IRecognitionCache>());
        await library.InitializeAsync();
        var sync = new SyncManager(trackers, queue, initializer.Object, history, background,
            settingsService: settings, progressChangeRepository: new ProgressChangeRepository(factory));
        var writer = new LibraryEntryWriter(library, sync, gate);
        var actions = new AnimeListActionService(library, sync, Mock.Of<IProgressUpdateService>(), history,
            dispatcher, trackers, savedLibrary, settings, entryWriter: writer, eventBus: new CoreEventBus());
        var item = new AnimeEntity { Id = AnimeId, MediaKind = kind, Title = "Example title", Notes = "Initial notes" };

        try
        {
            await FailQueueWritesAsync();
            Assert.False((await actions.AddToListAsync(item, UserAnimeStatus.Watching, 2, ct)).Success);
            Assert.Equal(UserAnimeStatus.None, item.Status);
            await AssertEntryMissingAsync();
            await AssertCountsAsync(0, 0);

            await AllowQueueWritesAsync();
            Assert.True((await actions.AddToListAsync(item, UserAnimeStatus.Watching, 2, ct)).Success);
            await AssertCountsAsync(1, 1);
            var canonical = library.FindById(AnimeId, kind);
            Assert.NotNull(canonical);
            var edited = canonical.Clone();
            edited.Status = UserAnimeStatus.OnHold;
            edited.UserScore = 8;
            edited.Notes = "Edited notes";

            await FailQueueWritesAsync();
            Assert.False((await actions.SaveAnimeAsync(canonical, edited, ct)).Success);
            Assert.Equal(UserAnimeStatus.Watching, canonical.Status);
            await AssertSavedEntryAsync(UserAnimeStatus.Watching, "Initial notes", null);
            await AssertCountsAsync(1, 1);

            await AllowQueueWritesAsync();
            Assert.True((await actions.SaveAnimeAsync(canonical, edited, ct)).Success);
            await AssertSavedEntryAsync(UserAnimeStatus.OnHold, "Edited notes", 8);
            await AssertCountsAsync(3, 2);

            await FailQueueWritesAsync();
            Assert.False((await actions.RemoveFromListAsync(AnimeId, kind, ct)).Success);
            Assert.Same(canonical, library.FindById(AnimeId, kind));
            await AssertSavedEntryAsync(UserAnimeStatus.OnHold, "Edited notes", 8);
            await AssertCountsAsync(3, 2);

            await AllowQueueWritesAsync();
            Assert.True((await actions.RemoveFromListAsync(AnimeId, kind, ct)).Success);
            await AssertEntryMissingAsync();
            await AssertCountsAsync(4, 3);
            Assert.NotNull(await savedLibrary.GetByIdAsync(AnimeId, siblingKind, ct));

            var addedAgain = new AnimeEntity { Id = AnimeId, MediaKind = kind, Title = item.Title, Notes = "Re-added notes" };
            Assert.True((await actions.AddToListAsync(addedAgain, UserAnimeStatus.PlanToWatch, 0, ct)).Success);
            await AssertSavedEntryAsync(UserAnimeStatus.PlanToWatch, "Re-added notes", null);
            await AssertCountsAsync(5, 4);
        }
        finally
        {
            await sync.StopAsync(CancellationToken.None);
        }

        var restored = new SyncManager(trackers, queue, initializer.Object, history, background, settingsService: settings);
        tracker.Setup(t => t.SaveFullListStatusAsync(It.Is<AnimeEntity>(entry =>
                entry.Id == AnimeId && entry.MediaKind == kind &&
                entry.Status == UserAnimeStatus.PlanToWatch && entry.Notes == "Re-added notes"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);
        try
        {
            await restored.StartAsync(ct);
            await WaitForQueueAsync(queue, rows => rows.Count == 0);
            tracker.Verify(t => t.SaveFullListStatusAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()), Times.Once);
            tracker.Verify(t => t.RemoveAnimeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            tracker.Verify(t => t.RemoveMangaAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            await restored.StopAsync(CancellationToken.None);
            await history.FlushAsync(Timeout);
            await background.StopAsync(ct);
        }

        async Task AssertCountsAsync(int historyCount, int tasks)
        {
            Assert.Equal(historyCount, (await history.GetHistoryAsync(ct: ct)).Count);
            Assert.Equal(tasks, (await queue.GetPendingAsync(ct)).Count);
        }

        async Task AssertSavedEntryAsync(UserAnimeStatus status, string notes, int? score)
        {
            var row = await savedLibrary.GetByIdAsync(AnimeId, kind, ct);
            Assert.NotNull(row);
            Assert.Equal((status, notes, score), (row.Status, row.Notes, row.UserScore));
        }

        async Task AssertEntryMissingAsync()
        {
            Assert.Null(library.FindById(AnimeId, kind));
            Assert.Null(await savedLibrary.GetByIdAsync(AnimeId, kind, ct));
        }

        async Task FailQueueWritesAsync()
        {
            await using var context = factory.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER refuse_library_task BEFORE INSERT ON sync_tasks
                BEGIN
                    SELECT RAISE(ABORT, 'Library mutation queue unavailable');
                END;
                """, ct);
        }

        async Task AllowQueueWritesAsync()
        {
            await using var context = factory.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_library_task", ct);
        }
    }
}
