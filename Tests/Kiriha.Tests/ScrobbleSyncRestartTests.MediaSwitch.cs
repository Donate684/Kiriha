using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Repository;
using Kiriha.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task MediaSwitch_LateRecognition_ScrobblesOnlyTheCurrentTitle(bool useInternalPlayer, bool switchPlayers)
    {
        const int firstId = AnimeId;
        const int secondId = AnimeId + 1;
        const string firstFile = "first-01.mkv";
        string secondFile = switchPlayers ? firstFile : "second-01.mkv";
        var factory = CreateFactory();
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var first = new AnimeEntity { Id = firstId, Title = "Hagane no Renkinjutsushi", Status = UserAnimeStatus.Watching };
        var second = new AnimeEntity { Id = secondId, Title = "Sousou no Frieren", Status = UserAnimeStatus.Watching };
        var savedLibrary = new UserAnimeRepository(factory);
        await savedLibrary.UpsertAsync(first);
        await savedLibrary.UpsertAsync(second);

        var library = new Mock<IAnimeRepository>();
        library.SetupGet(r => r.InitializationTask).Returns(Task.CompletedTask);
        library.Setup(r => r.GetSnapshotAsync()).ReturnsAsync(new[] { first, second });
        library.Setup(r => r.FindById(firstId, MediaKind.Anime)).Returns(first);
        library.Setup(r => r.FindById(secondId, MediaKind.Anime)).Returns(second);

        // The first lookup spans a file switch. Reopening the same filename must not revive it.
        var oldRecognition = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reopenedRecognition = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapping = new Mock<IMappingService>();
        mapping.SetupSequence(s => s.GetIdFromTitleAsync(firstFile, It.IsAny<IEnumerable<AnimeEntity>>()))
            .Returns(oldRecognition.Task)
            .Returns(reopenedRecognition.Task);
        if (!switchPlayers)
        {
            mapping.Setup(s => s.GetIdFromTitleAsync(secondFile, It.IsAny<IEnumerable<AnimeEntity>>()))
                .ReturnsAsync(secondId);
        }

        var countdowns = new List<Task>();
        var background = new Mock<IBackgroundTaskSupervisor>();
        background.Setup(x => x.Run("ScrobbleService.Countdown", It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task> work, CancellationToken ct) =>
            {
                var task = work(ct);
                countdowns.Add(task);
                return task;
            });
        var queue = new SyncTaskRepository(factory);
        var history = new HistoryService(new HistoryRepository(factory));
        var sync = CreateSyncManager(factory, queue, history, background.Object);
        var dispatcher = new ImmediateProgressDispatcher();
        var events = new CoreEventBus();
        var matchedIds = new List<int>();
        using var subscription = events.Subscribe<AnimeMatchedMessage>(message =>
        {
            if (message.Anime is { } matched)
                matchedIds.Add(matched.Id);
        });
        var settings = new SettingsService(Path.Combine(_directory.FullName, "test-settings.json"));
        settings.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.Mode = ScrobbleDelayMode.Time;
            s.System.Scrobbler.DelaySeconds = 0;
        }, save: false);
        using var progress = new AnimeProgressService(library.Object, savedLibrary, sync, history, dispatcher, eventBus: events);
        using var scrobbler = new ScrobbleService(
            progress, settings, Mock.Of<INotificationService>(), background.Object, dispatcher, ILocalizer.Passthrough, eventBus: events);
        using var tracking = new TrackingService(
            Mock.Of<IExternalMediaDetector>(), Mock.Of<IInternalPlayerServer>(), mapping.Object, library.Object,
            settings, Mock.Of<IDiscordService>(), scrobbler, dispatcher, [],
            new MediaMatchingPipeline(mapping.Object, []), eventBus: events);

        var old = OpenAsync(firstFile, useInternalPlayer);
        Task? reopened = null;
        try
        {
            Assert.False(old.IsCompleted);
            await OpenAsync(secondFile, switchPlayers || useInternalPlayer, switchPlayers ? secondId : null).WaitAsync(Timeout);
            await Task.WhenAll(countdowns).WaitAsync(Timeout);
            if (!switchPlayers)
                reopened = OpenAsync(firstFile, useInternalPlayer);

            oldRecognition.SetResult(firstId);
            await old.WaitAsync(Timeout);
            reopenedRecognition.SetResult(null);
            if (reopened != null)
                await reopened.WaitAsync(Timeout);
            if (switchPlayers)
                tracking.NotifyCurrentMediaMetadataUpdated(first);
            await Task.WhenAll(countdowns).WaitAsync(Timeout);

            _ = Assert.Single(countdowns);
            Assert.Equal(new[] { secondId }, matchedIds);
            Assert.Equal(switchPlayers ? secondId : (int?)null, tracking.MatchedAnime?.Id);
            Assert.Equal(0, (await savedLibrary.GetByIdAsync(firstId))?.Progress);
            Assert.Equal(1, (await savedLibrary.GetByIdAsync(secondId))?.Progress);
            var entry = Assert.Single(await history.GetHistoryAsync());
            Assert.Equal(secondId, entry.AnimeId);
            Assert.Equal(1, entry.Episode);
            Assert.Equal(secondId, Assert.Single(await queue.GetPendingAsync()).AnimeId);
        }
        finally
        {
            oldRecognition.TrySetResult(null);
            reopenedRecognition.TrySetResult(null);
            await sync.StopAsync(CancellationToken.None);
        }

        Task OpenAsync(string file, bool builtIn, int? explicitId = null) => builtIn
            ? tracking.SetInternalMediaAsync(new InternalPlayerState
            {
                OriginalTitle = file, AnimeTitle = file, Episode = "1", AnimeId = explicitId, IsPlaying = true
            })
            : tracking.ProcessDetectedMediaAsync(new ParsedMedia
            {
                OriginalTitle = file, AnimeTitle = file, Episode = "1", ProcessName = "mpv", Pid = 1, IsPlaying = true
            });
    }
}
