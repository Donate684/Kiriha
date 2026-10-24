using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Settings;
using Moq;

namespace Kiriha.Tests;

public class ScrobbleServiceTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly SettingsService _settingsService;
    private readonly Mock<AnimeProgressService> _mockProgressService;
    private readonly Mock<HistoryService> _mockHistoryService;
    private readonly Mock<NotificationService> _mockNotificationService;
    private readonly Mock<IBackgroundTaskSupervisor> _mockBackgroundTasks;
    private readonly Mock<IUiDispatcher> _mockUiDispatcher;
    private readonly Mock<ILocalizer> _mockLocalizer;
    private readonly Mock<IScrobbleProgressStore> _mockProgressStore;
    private readonly ScrobbleService _scrobbleService;

    public ScrobbleServiceTests()
    {
        _tempSettingsPath = Path.GetTempFileName();
        _settingsService = new SettingsService(_tempSettingsPath);

        // Setup initial settings
        _settingsService.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.DelaySeconds = 0; // immediate for testing
            s.System.Scrobbler.NotifyOnSkippedEpisode = true;
        }, save: false);

        _mockProgressService = new Mock<AnimeProgressService>(null!, null!, null!, null!, null!);
        _mockHistoryService = new Mock<HistoryService>(null!);
        _mockNotificationService = new Mock<NotificationService>(null!, null!);
        _mockBackgroundTasks = new Mock<IBackgroundTaskSupervisor>();
        _mockUiDispatcher = new Mock<IUiDispatcher>();
        _mockLocalizer = new Mock<ILocalizer>();
        _mockProgressStore = new Mock<IScrobbleProgressStore>();

        // When background task is queued, run it synchronously for testing
        _mockBackgroundTasks
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                task(ct).GetAwaiter().GetResult();
            });

        _scrobbleService = new ScrobbleService(
            _mockProgressService.Object,
            _mockHistoryService.Object,
            _settingsService,
            _mockNotificationService.Object,
            _mockBackgroundTasks.Object,
            _mockUiDispatcher.Object,
            _mockLocalizer.Object,
            _mockProgressStore.Object);
    }

    [Fact]
    public void StartScrobble_AlreadyScrobbled_DoesNothing()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "5" };
        var match = new AnimeEntity { Progress = 5 };

        bool statusUpdated = false;
        _scrobbleService.CountdownUpdated += (s, e) => statusUpdated = true;

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert
        Assert.True(statusUpdated);
        _mockProgressService.Verify(x => x.UpdateProgressAsync(It.IsAny<AnimeEntity>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>()), Times.Never);
        _mockBackgroundTasks.Verify(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void StartScrobble_SkippedEpisode_NotifiesAndDoesNothing()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "7" };
        var match = new AnimeEntity { Progress = 5 };

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert
        _mockNotificationService.Verify(x => x.NotifyScrobbleSkipped(match, 7), Times.Once);
        _mockProgressService.Verify(x => x.UpdateProgressAsync(It.IsAny<AnimeEntity>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>()), Times.Never);
        _mockBackgroundTasks.Verify(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void StartScrobble_ValidEpisode_UpdatesProgress()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "6", IsPlaying = true };
        var match = new AnimeEntity { Progress = 5, Id = 1, Title = "Test Anime" };

        _mockProgressService
            .Setup(x => x.UpdateProgressAsync(match, 6, It.IsAny<UserAnimeStatus?>()))
            .ReturnsAsync(true);

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert
        _mockBackgroundTasks.Verify(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockProgressService.Verify(x => x.UpdateProgressAsync(match, 6, It.IsAny<UserAnimeStatus?>()), Times.Once);
        _mockHistoryService.Verify(x => x.AddEntryAsync(match.Id, match.Title, match.RussianTitle, 6, "Scrobbled", null, match.MainPictureUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void StartScrobble_CompletesAnime_SetsStatusToCompleted()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "12", IsPlaying = true };
        var match = new AnimeEntity { Progress = 11, TotalEpisodes = 12, Id = 1, Title = "Test Anime" };

        _mockProgressService
            .Setup(x => x.UpdateProgressAsync(match, 12, UserAnimeStatus.Completed))
            .ReturnsAsync(true);

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert
        _mockProgressService.Verify(x => x.UpdateProgressAsync(match, 12, UserAnimeStatus.Completed), Times.Once);
        _mockHistoryService.Verify(x => x.AddEntryAsync(match.Id, match.Title, match.RussianTitle, 12, "Completed", null, match.MainPictureUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void StartScrobble_PlanToWatch_AutoStartsWatching()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "1", IsPlaying = true };
        var match = new AnimeEntity { Status = UserAnimeStatus.PlanToWatch, Progress = 0, Id = 10, Title = "Plan Anime" };

        _mockProgressService
            .Setup(x => x.UpdateProgressAsync(match, 1, UserAnimeStatus.Watching))
            .ReturnsAsync(true);

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert - automatically transitions from PlanToWatch to Watching with Progress = 1
        _mockProgressService.Verify(x => x.UpdateProgressAsync(match, 1, UserAnimeStatus.Watching), Times.Once);
        _mockHistoryService.Verify(x => x.AddEntryAsync(match.Id, match.Title, match.RussianTitle, 1, "Scrobbled", null, match.MainPictureUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void StartScrobble_DroppedAnime_AutoStartsWatching()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "2", IsPlaying = true };
        var match = new AnimeEntity { Status = UserAnimeStatus.Dropped, Progress = 1, Id = 11, Title = "Dropped Anime" };

        _mockProgressService
            .Setup(x => x.UpdateProgressAsync(match, 2, UserAnimeStatus.Watching))
            .ReturnsAsync(true);

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert - automatically transitions from Dropped to Watching with Progress = 2
        _mockProgressService.Verify(x => x.UpdateProgressAsync(match, 2, UserAnimeStatus.Watching), Times.Once);
        _mockHistoryService.Verify(x => x.AddEntryAsync(match.Id, match.Title, match.RussianTitle, 2, "Scrobbled", null, match.MainPictureUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void StartScrobble_OnHoldAnime_AutoStartsWatching()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "3", IsPlaying = true };
        var match = new AnimeEntity { Status = UserAnimeStatus.OnHold, Progress = 2, Id = 12, Title = "OnHold Anime" };

        _mockProgressService
            .Setup(x => x.UpdateProgressAsync(match, 3, UserAnimeStatus.Watching))
            .ReturnsAsync(true);

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert - automatically transitions from OnHold to Watching with Progress = 3
        _mockProgressService.Verify(x => x.UpdateProgressAsync(match, 3, UserAnimeStatus.Watching), Times.Once);
        _mockHistoryService.Verify(x => x.AddEntryAsync(match.Id, match.Title, match.RussianTitle, 3, "Scrobbled", null, match.MainPictureUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void StartScrobble_CompletedAnime_Episode1_PromptsRewatchWithoutAutoScrobble()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "1", IsPlaying = true };
        var match = new AnimeEntity { Status = UserAnimeStatus.Completed, Progress = 12, TotalEpisodes = 12, Id = 20, Title = "Completed Anime", IsRewatching = false };

        Kiriha.Core.Abstractions.Messages.AnimeRewatchPromptMessage? receivedPrompt = null;
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<Kiriha.Core.Abstractions.Messages.AnimeRewatchPromptMessage>(
            this, (r, m) => receivedPrompt = m);

        try
        {
            // Act
            _scrobbleService.StartScrobble(media, match);

            // Assert
            Assert.NotNull(receivedPrompt);
            Assert.Equal(match.Id, receivedPrompt.Anime.Id);
            Assert.Equal(1, receivedPrompt.Episode);
            // Must NOT auto-update progress before user confirms
            _mockProgressService.Verify(x => x.UpdateProgressAsync(It.IsAny<AnimeEntity>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>()), Times.Never);
        }
        finally
        {
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Unregister<Kiriha.Core.Abstractions.Messages.AnimeRewatchPromptMessage>(this);
        }
    }

    [Fact]
    public void StartScrobble_CompletesAnime_SendsRatingPromptAndNotification()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "12", IsPlaying = true };
        var match = new AnimeEntity { Status = UserAnimeStatus.Watching, Progress = 11, TotalEpisodes = 12, Id = 30, Title = "Finale Anime" };

        Kiriha.Core.Abstractions.Messages.AnimeCompletedRatingPromptMessage? receivedRatingPrompt = null;
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<Kiriha.Core.Abstractions.Messages.AnimeCompletedRatingPromptMessage>(
            this, (r, m) => receivedRatingPrompt = m);

        try
        {
            // Act
            _scrobbleService.StartScrobble(media, match);

            // Assert
            Assert.NotNull(receivedRatingPrompt);
            Assert.Equal(match.Id, receivedRatingPrompt.Anime.Id);
            _mockNotificationService.Verify(x => x.NotifyAnimeCompleted(match), Times.Once);
        }
        finally
        {
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Unregister<Kiriha.Core.Abstractions.Messages.AnimeCompletedRatingPromptMessage>(this);
        }
    }

    [Fact]
    public void CancelScrobble_CancelsInProgressScrobble()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "6", IsPlaying = true };
        var match = new AnimeEntity { Progress = 5 };

        // Make the background task not complete immediately so we can cancel it
        _settingsService.Update(s => s.System.Scrobbler.DelaySeconds = 10, save: false);

        CancellationToken? taskToken = null;
        _mockBackgroundTasks
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                taskToken = ct;
            });

        _scrobbleService.StartScrobble(media, match);

        Assert.NotNull(taskToken);
        Assert.False(taskToken.Value.IsCancellationRequested);

        // Act
        _scrobbleService.CancelScrobble();

        // Assert
        Assert.True(taskToken.Value.IsCancellationRequested);
    }

    [Fact]
    public async Task ScrobbleService_PercentageMode_KnownDuration_SeekDoesNotFastScrobbleOrFreezeTimer()
    {
        // Arrange
        _settingsService.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.Mode = ScrobbleDelayMode.Percentage;
            s.System.Scrobbler.DelayPercent = 50;
        }, save: false);

        var bgMock = new Mock<IBackgroundTaskSupervisor>();
        bgMock
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                Task.Run(() => task(ct));
            });

        using var service = new ScrobbleService(
            _mockProgressService.Object,
            _mockHistoryService.Object,
            _settingsService,
            _mockNotificationService.Object,
            bgMock.Object,
            _mockUiDispatcher.Object,
            _mockLocalizer.Object,
            _mockProgressStore.Object);

        var firstTickTcs = new TaskCompletionSource<string>();
        var secondTickTcs = new TaskCompletionSource<string>();

        service.CountdownUpdated += (s, msg) =>
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (!firstTickTcs.Task.IsCompleted)
            {
                firstTickTcs.TrySetResult(msg);
            }
            else
            {
                secondTickTcs.TrySetResult(msg);
            }
        };

        var media = new ParsedMedia
        {
            Episode = "1",
            Duration = TimeSpan.FromSeconds(100),
            Position = TimeSpan.Zero,
            IsPlaying = true
        };
        var match = new AnimeEntity { Id = 10, Progress = 0, TotalEpisodes = 12 };

        // Act 1: start scrobble -> should emit 00:50 (50% of 100s)
        service.StartScrobble(media, match);

        var firstResult = await Task.WhenAny(firstTickTcs.Task, Task.Delay(10000));
        Assert.Same(firstTickTcs.Task, firstResult);
        Assert.Equal("00:50", await firstTickTcs.Task);

        // Act 2: seek position forward to 90s (past the 50% threshold)
        service.UpdatePlaybackProgress(true, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(100));

        // Must not scrobble immediately on seek
        await Task.Delay(200);
        _mockProgressService.Verify(x => x.UpdateProgressAsync(It.IsAny<AnimeEntity>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>()), Times.Never);

        // Assert: countdown continues decrementing and does not freeze
        var secondResult = await Task.WhenAny(secondTickTcs.Task, Task.Delay(10000));
        Assert.Same(secondTickTcs.Task, secondResult);
        Assert.Equal("00:49", await secondTickTcs.Task);

        service.CancelScrobble();
        _mockProgressService.Verify(x => x.UpdateProgressAsync(It.IsAny<AnimeEntity>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>()), Times.Never);
    }

    [Fact]
    public async Task ScrobbleService_PercentageMode_WithoutDuration_UsesDefaultEpisodeDuration()
    {
        // Arrange: 50% threshold, Duration = null (external player without SMTC), DelaySeconds = 300 (should be ignored)
        _settingsService.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.Mode = ScrobbleDelayMode.Percentage;
            s.System.Scrobbler.DelayPercent = 50;
            s.System.Scrobbler.DelaySeconds = 300;
        }, save: false);

        var bgMock = new Mock<IBackgroundTaskSupervisor>();
        bgMock
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                Task.Run(() => task(ct));
            });

        using var service = new ScrobbleService(
            _mockProgressService.Object,
            _mockHistoryService.Object,
            _settingsService,
            _mockNotificationService.Object,
            bgMock.Object,
            _mockUiDispatcher.Object,
            _mockLocalizer.Object,
            _mockProgressStore.Object);

        var firstTickTcs = new TaskCompletionSource<string>();
        service.CountdownUpdated += (s, msg) =>
        {
            if (!string.IsNullOrEmpty(msg) && !firstTickTcs.Task.IsCompleted)
            {
                firstTickTcs.TrySetResult(msg);
            }
        };

        var media = new ParsedMedia
        {
            Episode = "1",
            Duration = null,
            IsPlaying = true
        };
        var match = new AnimeEntity { Id = 10, Progress = 0, TotalEpisodes = 12 };

        // Act
        service.StartScrobble(media, match);

        // Assert: 50% of 24 min (1440s) = 720s -> 12:00 (not 05:00 from DelaySeconds)
        var completed = await Task.WhenAny(firstTickTcs.Task, Task.Delay(10000));
        Assert.Same(firstTickTcs.Task, completed);
        Assert.Equal("12:00", await firstTickTcs.Task);

        service.CancelScrobble();
    }

    [Fact]
    public async Task ScrobbleService_PercentageMode_DynamicDurationUpdate_RecalculatesTarget()
    {
        // Arrange
        _settingsService.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.Mode = ScrobbleDelayMode.Percentage;
            s.System.Scrobbler.DelayPercent = 50;
        }, save: false);

        var bgMock = new Mock<IBackgroundTaskSupervisor>();
        bgMock
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                Task.Run(() => task(ct));
            });

        using var service = new ScrobbleService(
            _mockProgressService.Object,
            _mockHistoryService.Object,
            _settingsService,
            _mockNotificationService.Object,
            bgMock.Object,
            _mockUiDispatcher.Object,
            _mockLocalizer.Object,
            _mockProgressStore.Object);

        var firstTickTcs = new TaskCompletionSource<string>();
        var updatedTickTcs = new TaskCompletionSource<string>();

        service.CountdownUpdated += (s, msg) =>
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (!firstTickTcs.Task.IsCompleted)
            {
                firstTickTcs.TrySetResult(msg);
            }
            else if (msg.StartsWith("10:") || msg.StartsWith("09:"))
            {
                updatedTickTcs.TrySetResult(msg);
            }
        };

        var media = new ParsedMedia
        {
            Episode = "1",
            Duration = TimeSpan.Zero, // Initially 0
            IsPlaying = true
        };
        var match = new AnimeEntity { Id = 10, Progress = 0, TotalEpisodes = 12 };

        // Act 1: start with 0 duration -> fallback to 24 min (12:00)
        service.StartScrobble(media, match);

        var first = await Task.WhenAny(firstTickTcs.Task, Task.Delay(10000));
        Assert.Same(firstTickTcs.Task, first);
        Assert.Equal("12:00", await firstTickTcs.Task);

        // Act 2: player sends updated duration = 1200s (20 min)
        service.UpdatePlaybackProgress(true, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1200));

        // Assert: target automatically recalculates to 50% of 1200s = 600s (10:00 or 09:59)
        var updated = await Task.WhenAny(updatedTickTcs.Task, Task.Delay(10000));
        Assert.Same(updatedTickTcs.Task, updated);
        var updatedMsg = await updatedTickTcs.Task;
        Assert.True(updatedMsg.StartsWith("10:") || updatedMsg.StartsWith("09:"), $"Expected 10:xx or 09:xx but got {updatedMsg}");

        service.CancelScrobble();
    }

    public void Dispose()
    {
        _scrobbleService.Dispose();
        _settingsService.Dispose();
        if (File.Exists(_tempSettingsPath))
        {
            try { File.Delete(_tempSettingsPath); } catch { }
        }
    }
}

