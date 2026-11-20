using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Services;
using Kiriha.Infrastructure.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class ScrobbleTimerAndResumeTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly SettingsService _settingsService;
    private readonly Mock<IProgressUpdateService> _mockProgressService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IBackgroundTaskSupervisor> _mockBackgroundTasks;
    private readonly Mock<IUiDispatcher> _mockUiDispatcher;
    private readonly Mock<ILocalizer> _mockLocalizer;

    public ScrobbleTimerAndResumeTests()
    {
        _tempSettingsPath = Path.GetTempFileName();
        _settingsService = new SettingsService(_tempSettingsPath);

        _mockProgressService = new Mock<IProgressUpdateService>();
        _mockProgressService.Setup(x => x.RecordScrobbleAsync(
            It.IsAny<AnimeEntity>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockNotificationService = new Mock<INotificationService>();
        _mockBackgroundTasks = new Mock<IBackgroundTaskSupervisor>();
        _mockUiDispatcher = new Mock<IUiDispatcher>();
        _mockLocalizer = new Mock<ILocalizer>();

        _mockBackgroundTasks
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                task(ct).GetAwaiter().GetResult();
            });

        _mockUiDispatcher
            .Setup(x => x.InvokeAsync(It.IsAny<Func<bool>>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public void ScrobbleService_PercentageMode_ScrobblesWhenReachingThreshold()
    {
        // Arrange: 50% threshold on a 100s video (target 50s), 50s already elapsed
        _settingsService.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.Mode = ScrobbleDelayMode.Percentage;
            s.System.Scrobbler.DelayPercent = 50;
        }, save: false);

        var mockStore = new Mock<IScrobbleProgressStore>();
        mockStore.Setup(x => x.GetElapsed("10_1")).Returns(50);

        var service = new ScrobbleService(
            _mockProgressService.Object,
            _settingsService,
            _mockNotificationService.Object,
            _mockBackgroundTasks.Object,
            _mockUiDispatcher.Object,
            _mockLocalizer.Object,
            mockStore.Object);

        var media = new ParsedMedia
        {
            Episode = "1",
            Duration = TimeSpan.FromSeconds(100),
            Position = TimeSpan.FromSeconds(50),
            IsPlaying = true
        };
        var match = new AnimeEntity { Id = 10, Progress = 0, TotalEpisodes = 12 };

        // Act
        service.StartScrobble(media, match);

        // Assert: Scrobbler reached 50% threshold and invoked progress update
        _mockProgressService.Verify(x => x.RecordScrobbleAsync(match, 1, It.IsAny<UserAnimeStatus?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ScrobbleService_ResumesFromSavedElapsedProgress()
    {
        // Arrange: timer delay 300s, but 300s was already saved in progress store
        _settingsService.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.Mode = ScrobbleDelayMode.Time;
            s.System.Scrobbler.DelaySeconds = 300;
        }, save: false);

        var mockStore = new Mock<IScrobbleProgressStore>();
        mockStore.Setup(x => x.GetElapsed("10_1")).Returns(300);

        var service = new ScrobbleService(
            _mockProgressService.Object,
            _settingsService,
            _mockNotificationService.Object,
            _mockBackgroundTasks.Object,
            _mockUiDispatcher.Object,
            _mockLocalizer.Object,
            mockStore.Object);

        var media = new ParsedMedia
        {
            Episode = "1",
            IsPlaying = true
        };
        var match = new AnimeEntity { Id = 10, Progress = 0, TotalEpisodes = 12 };

        // Act
        service.StartScrobble(media, match);

        // Assert: because 300s were restored from store, remaining <= 0 immediately
        _mockProgressService.Verify(x => x.RecordScrobbleAsync(match, 1, It.IsAny<UserAnimeStatus?>(), It.IsAny<CancellationToken>()), Times.Once);
        mockStore.Verify(x => x.Clear("10_1"), Times.Once);
    }

    [Fact]
    public async Task ScrobbleProgressStore_SavesRetrievesAndClears()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var initCtx = new AppDbContext(options))
        {
            await initCtx.Database.EnsureCreatedAsync();
        }

        var factory = new SimpleDbContextFactory(options);
        var store = new SqliteScrobbleProgressStore(factory);
        Assert.Equal(0, store.GetElapsed("test_key"));

        store.SaveElapsed("test_key", 125);
        Assert.Equal(125, store.GetElapsed("test_key"));
        await store.FlushAsync();

        // New instance reading same SQLite database
        var store2 = new SqliteScrobbleProgressStore(factory);
        Assert.Equal(125, store2.GetElapsed("test_key"));

        store2.Clear("test_key");
        Assert.Equal(0, store2.GetElapsed("test_key"));
        await store2.FlushAsync();

        var store3 = new SqliteScrobbleProgressStore(factory);
        Assert.Equal(0, store3.GetElapsed("test_key"));
    }

    [Fact]
    public void PlaybackResumeStore_SavesAndRetrievesPosition()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"playback_resume_{Guid.NewGuid():N}.json");
        try
        {
            var store = new FilePlaybackResumeStore(tempFile);
            string testUrl = "http://example.com/test_video.mkv";
            store.SavePosition(testUrl, 142.5, 300.0);

            var pos = store.TryGetResumePosition(testUrl, Path.GetTempPath());
            Assert.NotNull(pos);
            Assert.Equal(142.5, pos.Value, 1);

            store.Remove(testUrl);
            var posAfterRemove = store.TryGetResumePosition(testUrl, Path.GetTempPath());
            Assert.Null(posAfterRemove);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private sealed class SimpleDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    public void Dispose()
    {
        _settingsService.Dispose();
        if (File.Exists(_tempSettingsPath))
        {
            try { File.Delete(_tempSettingsPath); } catch { }
        }
    }
}