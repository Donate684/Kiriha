using Moq;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;

namespace Kiriha.Tests;

public sealed class TrackerOrchestrationTests
{
    [Fact]
    public async Task TrackerRefresh_DoesNotReAddEntry_WhenRemovalIsAcknowledgedDuringFetch()
    {
        var anime = new AnimeEntity { Id = 51, Title = "Deleted entry", Status = UserAnimeStatus.Watching };
        var library = new Mock<IAnimeRepository>();
        library.Setup(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>())).ReturnsAsync([]);
        var repository = new Mock<IUserAnimeRepository>();
        var queue = new Mock<ISyncTaskRepository>();
        queue.SetupSequence(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SyncTaskEntity { Id = 1, AnimeId = anime.Id, Type = "Remove" }])
            .ReturnsAsync([]);
        var tracker = new Mock<ITrackerService>();
        tracker.SetupGet(t => t.IsEnabled).Returns(true);
        tracker.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([anime]);
        var sync = new AnimeSyncOrchestrator(library.Object, repository.Object, [tracker.Object],
            Mock.Of<IRecognitionCache>(), syncTaskRepo: queue.Object);

        Assert.True(await sync.SyncWithTrackersAsync());
        library.Verify(r => r.ApplySyncBatchAsync(It.IsAny<List<AnimeEntity>>(), It.IsAny<List<Action>>()), Times.Never);
        repository.Verify(r => r.SyncFromRemoteAsync(
            It.Is<IEnumerable<AnimeEntity>>(items => !items.Any()),
            It.IsAny<MediaKind[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnimeSyncOrchestrator_SelectsConfiguredPrimaryTracker()
    {
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        mockAnimeRepo.Setup(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>()))
            .ReturnsAsync(new List<AnimeEntity>());
        mockAnimeRepo.Setup(r => r.GetSnapshotAsync())
            .ReturnsAsync(new List<AnimeEntity>());

        var mockUserAnimeRepo = new Mock<IUserAnimeRepository>();
        var mockRecognitionCache = new Mock<IRecognitionCache>();

        // Tracker 1: MAL (enabled)
        var mockMal = new Mock<ITrackerService>();
        mockMal.Setup(t => t.Name).Returns("MyAnimeList");
        mockMal.Setup(t => t.TrackerId).Returns(TrackerConstants.Ids.Mal);
        mockMal.Setup(t => t.IsEnabled).Returns(true);
        mockMal.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AnimeEntity>());

        // Tracker 2: ShikiOrig (enabled)
        var mockShiki = new Mock<ITrackerService>();
        mockShiki.Setup(t => t.Name).Returns("Shikimori");
        mockShiki.Setup(t => t.TrackerId).Returns(TrackerConstants.Ids.ShikiOrig);
        mockShiki.Setup(t => t.IsEnabled).Returns(true);
        mockShiki.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AnimeEntity>());

        // Settings with ShikiOrig as Primary
        var settings = new AppSettings();
        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            Tokens = new MalTokens { AccessToken = "mal" },
            IsEnabled = true,
            IsPrimary = false,
            IsMirror = true
        });
        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            Tokens = new ShikiTokens { AccessToken = "shiki" },
            IsEnabled = true,
            IsPrimary = true,
            IsMirror = false
        });
        settings.Api.EnsurePrimaryIntegrity();

        var mockSettingsService = new Mock<ISettingsService>();
        mockSettingsService.Setup(s => s.Current).Returns(settings);

        var orchestrator = new AnimeSyncOrchestrator(
            mockAnimeRepo.Object,
            mockUserAnimeRepo.Object,
            new[] { mockMal.Object, mockShiki.Object },
            mockRecognitionCache.Object,
            mockSettingsService.Object);

        var result = await orchestrator.SyncWithTrackersAsync();

        Assert.True(result);

        // Verify that Shiki was called (because it is Primary), and MAL was NOT called for library fetch
        mockShiki.Verify(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockMal.Verify(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnimeSyncOrchestrator_DiscardsResponseFromPreviousAccount()
    {
        var library = new Mock<IAnimeRepository>();
        library.Setup(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>()))
            .ReturnsAsync([]);
        var repository = new Mock<IUserAnimeRepository>(MockBehavior.Strict);
        var tracker = new Mock<ITrackerService>();
        tracker.SetupGet(t => t.TrackerId).Returns(TrackerConstants.Ids.Mal);
        tracker.SetupGet(t => t.Name).Returns("MyAnimeList");
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<List<AnimeEntity>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) =>
            {
                requestStarted.SetResult();
                return response.Task.WaitAsync(ct);
            });
        var current = AccountSettings("first-user");
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(() => Volatile.Read(ref current));
        var sync = new AnimeSyncOrchestrator(library.Object, repository.Object, [tracker.Object],
            Mock.Of<IRecognitionCache>(), settings.Object);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var refresh = sync.SyncWithTrackersAsync(ct: timeout.Token);
        await requestStarted.Task.WaitAsync(timeout.Token);
        Volatile.Write(ref current, AccountSettings("second-user"));
        response.SetResult([new AnimeEntity { Id = 1, Title = "Previous user's list" }]);

        Assert.False(await refresh);
        repository.VerifyNoOtherCalls();
        library.Verify(r => r.ApplySyncBatchAsync(It.IsAny<List<AnimeEntity>>(), It.IsAny<List<Action>>()), Times.Never);

        static AppSettings AccountSettings(string id) => new()
        {
            Api = new AppSettings.ApiConfig
            {
                Accounts = [new TrackerAccount
                {
                    Id = id, TrackerId = TrackerConstants.Ids.Mal,
                    IsPrimary = true, Tokens = new MalTokens { AccessToken = id }
                }]
            }
        };
    }

    [Fact]
    public void TrackerId_MatchesConfiguredServiceIds()
    {
        var settings = new AppSettings();
        settings.Api.ShikiMirror = ShikiMirror.Net;

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var malTracker = new Mock<ITrackerService>();
        malTracker.Setup(t => t.TrackerId).Returns(TrackerConstants.Ids.Mal);
        Assert.Equal(TrackerConstants.Ids.Mal, malTracker.Object.TrackerId);

        var shikiOrigTracker = new Mock<ITrackerService>();
        shikiOrigTracker.Setup(t => t.TrackerId).Returns(TrackerConstants.Ids.ShikiOrig);
        Assert.Equal("shiki-orig", shikiOrigTracker.Object.TrackerId);

        var shikiForkTracker = new Mock<ITrackerService>();
        shikiForkTracker.Setup(t => t.TrackerId).Returns(TrackerConstants.Ids.ShikiFork);
        Assert.Equal("shiki-fork", shikiForkTracker.Object.TrackerId);
    }
}
