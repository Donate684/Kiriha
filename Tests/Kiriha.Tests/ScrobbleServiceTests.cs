using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Core;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Tracking.Integration;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Settings;
using Kiriha.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;
using Moq;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Messages;

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
            _mockLocalizer.Object);
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
        _mockHistoryService.Verify(x => x.AddEntry(match.Id, match.Title, match.RussianTitle, 6, "Scrobbled", null), Times.Once);
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
        _mockHistoryService.Verify(x => x.AddEntry(match.Id, match.Title, match.RussianTitle, 12, "Completed", null), Times.Once);
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
        _mockHistoryService.Verify(x => x.AddEntry(match.Id, match.Title, match.RussianTitle, 1, "Scrobbled", null), Times.Once);
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

