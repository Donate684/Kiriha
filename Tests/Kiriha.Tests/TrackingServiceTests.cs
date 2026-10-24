using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Services.Data.Mapping;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using Moq;

namespace Kiriha.Tests;

public class TrackingServiceTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly string _tempMappingPath;
    private readonly SettingsService _settingsService;
    private readonly Mock<IScrobbleService> _mockScrobbleService;
    private readonly Mock<IUiDispatcher> _mockUiDispatcher;
    private readonly Mock<MappingService> _mockMappingService;
    private readonly Mock<IDiscordService> _mockDiscordService;
    private readonly Mock<IExternalMediaDetector> _mockExternalMediaDetector;
    private readonly AnimeRepository _animeRepository;
    private readonly TrackingService _trackingService;

    public TrackingServiceTests()
    {
        _tempSettingsPath = Path.GetTempFileName();
        _tempMappingPath = Path.GetTempFileName();

        _settingsService = new SettingsService(_tempSettingsPath);
        _settingsService.Update(s => s.System.Scrobbler.Enabled = true, save: false);

        _mockScrobbleService = new Mock<IScrobbleService>();

        _mockUiDispatcher = new Mock<IUiDispatcher>();
        _mockUiDispatcher.Setup(x => x.Post(It.IsAny<Action>())).Callback<Action>(a => a());
        _mockUiDispatcher.Setup(x => x.InvokeAsync(It.IsAny<Func<List<AnimeEntity>>>()))
            .Returns<Func<List<AnimeEntity>>>(f => Task.FromResult(f()));

        _mockMappingService = new Mock<MappingService>(null!, new ManualMappingService(_tempMappingPath), null!, new RecognitionCache());
        _mockDiscordService = new Mock<IDiscordService>();
        _mockExternalMediaDetector = new Mock<IExternalMediaDetector>();

        _animeRepository = new AnimeRepository(null!, null!, null!, _mockUiDispatcher.Object, null!);
        // Force the initialization task to complete so we don't wait 5 seconds in tests
        var tcsField = typeof(AnimeRepository).GetField("_initTcs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (tcsField?.GetValue(_animeRepository) is TaskCompletionSource tcs)
        {
            tcs.TrySetResult();
        }

        _trackingService = new TrackingService(
            _mockExternalMediaDetector.Object,
            new Mock<IInternalPlayerServer>().Object,
            _mockMappingService.Object,
            _animeRepository,
            _settingsService,
            _mockDiscordService.Object,
            _mockScrobbleService.Object,
            _mockUiDispatcher.Object,
            Array.Empty<ITrackerService>(),
            new MediaMatchingPipeline(_mockMappingService.Object, Array.Empty<ITrackerService>())
        );
    }

    [Fact]
    public async Task SetInternalMedia_ValidMatch_StartsScrobble()
    {
        // Arrange
        var state = new InternalPlayerState
        {
            AnimeTitle = "Test Anime",
            OriginalTitle = "Test Anime [1080p].mkv",
            Episode = "1",
            IsPlaying = true,
            Position = 10,
            Duration = 1400
        };

        var match = new AnimeEntity { Id = 123, Title = "Test Anime", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        _mockMappingService
            .Setup(x => x.GetIdFromTitleAsync(state.OriginalTitle, It.IsAny<IEnumerable<AnimeEntity>>()))
            .ReturnsAsync(123);

        // Act
        await _trackingService.SetInternalMediaAsync(state);

        // Assert
        _mockScrobbleService.Verify(x => x.StartScrobble(It.Is<ParsedMedia>(m => m.AnimeTitle == "Test Anime"), match), Times.Once);
        _mockDiscordService.Verify(x => x.UpdatePresence(
            It.IsAny<string>(), state.Episode, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(), true), Times.Once);
    }

    [Fact]
    public async Task SetInternalMedia_WithExplicitId_StartsScrobbleDirectly()
    {
        // Arrange
        var state = new InternalPlayerState
        {
            AnimeTitle = "Explicit Anime",
            Episode = "2",
            IsPlaying = true,
            AnimeId = 456
        };

        var match = new AnimeEntity { Id = 456, Title = "Explicit Anime", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        // Act
        await _trackingService.SetInternalMediaAsync(state);

        // Assert
        _mockScrobbleService.Verify(x => x.StartScrobble(It.Is<ParsedMedia>(m => m.AnimeTitle == "Explicit Anime"), match), Times.Once);
        _mockMappingService.Verify(x => x.GetIdFromTitleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<AnimeEntity>>()), Times.Never);
    }

    [Fact]
    public async Task SetInternalMedia_NegativelyMapped_DoesNotMatch()
    {
        // Arrange
        var state = new InternalPlayerState
        {
            AnimeTitle = "Bad Anime",
            OriginalTitle = "Bad Anime",
            Episode = "1",
            IsPlaying = true
        };

        _mockMappingService.Setup(x => x.IsNegativelyMapped(state.OriginalTitle)).Returns(true);

        // Act
        await _trackingService.SetInternalMediaAsync(state);

        // Assert
        _mockMappingService.Verify(x => x.GetIdFromTitleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<AnimeEntity>>()), Times.Never);
        _mockScrobbleService.Verify(x => x.StartScrobble(It.IsAny<ParsedMedia>(), It.IsAny<AnimeEntity>()), Times.Never);
    }

    [Fact]
    public async Task SetInternalMedia_SendsMetadataWithTorrentHashAndEnrichesWithShiki()
    {
        // Arrange
        var mockPlayerServer = new Mock<IInternalPlayerServer>();
        var mockShiki = new Mock<IShikiMetadataService>();
        var match = new AnimeEntity { Id = 456, Title = "Sample Anime", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        mockShiki
            .Setup(x => x.EnsureLocalizedAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()))
            .Returns<AnimeEntity, CancellationToken>((a, _) =>
            {
                a.RussianTitle = "Localized Sample";
                return Task.CompletedTask;
            });

        using var service = new TrackingService(
            _mockExternalMediaDetector.Object,
            mockPlayerServer.Object,
            _mockMappingService.Object,
            _animeRepository,
            _settingsService,
            _mockDiscordService.Object,
            _mockScrobbleService.Object,
            _mockUiDispatcher.Object,
            Array.Empty<ITrackerService>(),
            new MediaMatchingPipeline(_mockMappingService.Object, Array.Empty<ITrackerService>()),
            mockShiki.Object
        );

        var state = new InternalPlayerState
        {
            AnimeTitle = "Sample Anime",
            OriginalTitle = "Sample.Anime.E01.mkv",
            Episode = "1",
            TorrentHash = "deadbeef1234",
            IsPlaying = true
        };

        _mockMappingService
            .Setup(x => x.GetIdFromTitleAsync(state.OriginalTitle, It.IsAny<IEnumerable<AnimeEntity>>()))
            .ReturnsAsync(456);

        // Act
        await service.SetInternalMediaAsync(state);

        // Assert: SendMetadataAsync was invoked with the matching TorrentHash
        mockPlayerServer.Verify(x => x.SendMetadataAsync(
            It.Is<PlayerMediaMetadata>(m => m.TorrentHash == "deadbeef1234"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartAsync_WhenPlayerServerHasExistingState_PicksUpStateAndSetsMedia()
    {
        // Arrange
        var mockPlayerServer = new Mock<IInternalPlayerServer>();
        var existingState = new InternalPlayerState
        {
            AnimeTitle = "Existing Anime",
            OriginalTitle = "Existing Anime [1080p].mkv",
            Episode = "2",
            IsPlaying = true,
            Position = 50,
            Duration = 1400
        };
        mockPlayerServer.Setup(x => x.LatestState).Returns(existingState);

        var match = new AnimeEntity { Id = 789, Title = "Existing Anime", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        _mockMappingService
            .Setup(x => x.GetIdFromTitleAsync(existingState.OriginalTitle, It.IsAny<IEnumerable<AnimeEntity>>()))
            .ReturnsAsync(789);

        using var service = new TrackingService(
            _mockExternalMediaDetector.Object,
            mockPlayerServer.Object,
            _mockMappingService.Object,
            _animeRepository,
            _settingsService,
            _mockDiscordService.Object,
            _mockScrobbleService.Object,
            _mockUiDispatcher.Object,
            Array.Empty<ITrackerService>(),
            new MediaMatchingPipeline(_mockMappingService.Object, Array.Empty<ITrackerService>())
        );

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(service.CurrentMedia);
        Assert.Equal("Existing Anime", service.CurrentMedia.AnimeTitle);
        Assert.Equal("2", service.CurrentMedia.Episode);
    }

    [Fact]
    public async Task SetInternalMedia_PauseAndResume_DoesNotCancelScrobble()
    {
        // Arrange
        var match = new AnimeEntity { Id = 500, Title = "Frieren", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        var playingState = new InternalPlayerState
        {
            AnimeId = 500,
            AnimeTitle = "Frieren",
            OriginalTitle = "Frieren_01.mkv",
            Episode = "1",
            IsPlaying = true,
            Position = 10,
            Duration = 1400
        };

        await _trackingService.SetInternalMediaAsync(playingState);
        _mockScrobbleService.Verify(x => x.StartScrobble(It.IsAny<ParsedMedia>(), match), Times.Once);
        _mockScrobbleService.Invocations.Clear();

        // Act 1: Pause
        var pausedState = new InternalPlayerState
        {
            AnimeId = 500,
            AnimeTitle = "Frieren",
            OriginalTitle = "Frieren_01.mkv",
            Episode = "1",
            IsPlaying = false,
            Position = 10,
            Duration = 1400
        };
        await _trackingService.SetInternalMediaAsync(pausedState);

        // Act 2: Resume
        var resumedState = new InternalPlayerState
        {
            AnimeId = 500,
            AnimeTitle = "Frieren",
            OriginalTitle = "Frieren_01.mkv",
            Episode = "1",
            IsPlaying = true,
            Position = 15,
            Duration = 1400
        };
        await _trackingService.SetInternalMediaAsync(resumedState);

        // Assert: CancelScrobble was NEVER called, and playing state was updated
        _mockScrobbleService.Verify(x => x.CancelScrobble(), Times.Never);
        _mockScrobbleService.Verify(x => x.UpdatePlayingState(false), Times.Once);
        _mockScrobbleService.Verify(x => x.UpdatePlayingState(true), Times.Once);
    }

    [Fact]
    public async Task ExternalDetectorEvents_WhenInternalPlayerActive_DoNotCancelOrOverrideScrobble()
    {
        // Arrange
        var match = new AnimeEntity { Id = 600, Title = "Dungeon Meshi", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        var internalState = new InternalPlayerState
        {
            AnimeId = 600,
            AnimeTitle = "Dungeon Meshi",
            OriginalTitle = "Dungeon_Meshi_01.mkv",
            Episode = "1",
            IsPlaying = true,
            Position = 100,
            Duration = 1400
        };
        await _trackingService.SetInternalMediaAsync(internalState);
        _mockScrobbleService.Invocations.Clear();

        // Act 1: External detector fires MediaCleared
        _mockExternalMediaDetector.Raise(x => x.MediaCleared += null, EventArgs.Empty);

        // Assert: CancelScrobble was NOT called because internal player is active
        _mockScrobbleService.Verify(x => x.CancelScrobble(), Times.Never);

        // Act 2: External detector fires MediaDetected for an external media
        _mockExternalMediaDetector.Raise(x => x.MediaDetected += null, _mockExternalMediaDetector.Object, new ParsedMedia
        {
            ProcessName = "mpv",
            AnimeTitle = "Other Anime",
            OriginalTitle = "Other Anime 01.mkv",
            Episode = "1",
            IsPlaying = false
        });

        // Assert: External detection was ignored; media remains internal
        Assert.Equal("Dungeon Meshi", _trackingService.CurrentMedia?.AnimeTitle);
        Assert.Equal("KirihaInternal", _trackingService.CurrentMedia?.ProcessName);
        _mockScrobbleService.Verify(x => x.CancelScrobble(), Times.Never);
    }

    [Fact]
    public async Task SetInternalMedia_EpisodeFormattingDifference_DoesNotCancelScrobble()
    {
        // Arrange
        var match = new AnimeEntity { Id = 700, Title = "Oshi no Ko", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        var state1 = new InternalPlayerState
        {
            AnimeId = 700,
            AnimeTitle = "Oshi no Ko",
            OriginalTitle = "Oshi_no_Ko_01.mkv",
            Episode = "01",
            IsPlaying = true,
            Position = 5,
            Duration = 1400
        };
        await _trackingService.SetInternalMediaAsync(state1);
        _mockScrobbleService.Invocations.Clear();

        // Act: Metadata enrichment normalizes episode to "1"
        var state2 = new InternalPlayerState
        {
            AnimeId = 700,
            AnimeTitle = "Oshi no Ko",
            OriginalTitle = "Oshi_no_Ko_01.mkv",
            Episode = "1",
            IsPlaying = true,
            Position = 10,
            Duration = 1400
        };
        await _trackingService.SetInternalMediaAsync(state2);

        // Assert: Should be treated as same media without cancellation
        _mockScrobbleService.Verify(x => x.CancelScrobble(), Times.Never);
    }

    public void Dispose()
    {
        _trackingService.Dispose();
        _settingsService.Dispose();
        if (File.Exists(_tempSettingsPath)) try { File.Delete(_tempSettingsPath); } catch { }
        if (File.Exists(_tempMappingPath)) try { File.Delete(_tempMappingPath); } catch { }
    }
}




