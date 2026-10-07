using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Sync;
using Moq;

namespace Kiriha.Tests;

public class AnimeRefreshServiceTests
{
    private readonly Mock<IAnimeSyncOrchestrator> _syncOrchestratorMock = new();
    private readonly Mock<IAiringInfoService> _airingServiceMock = new();
    private readonly AnimeRefreshService _service;

    public AnimeRefreshServiceTests()
    {
        _service = new AnimeRefreshService(_syncOrchestratorMock.Object, _airingServiceMock.Object);
    }

    [Fact]
    public async Task RefreshAnimeListAsync_WhenTrackerSyncFails_ReturnsFalse_AndDoesNotSyncAiring()
    {
        _syncOrchestratorMock.Setup(s => s.SyncWithTrackersAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(false);

        var result = await _service.RefreshAnimeListAsync();

        Assert.False(result);
        _airingServiceMock.Verify(a => a.SyncOngoingEpisodesAsync(It.IsAny<bool>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAnimeListAsync_WhenTrackerSyncSucceeds_RunsAiringSyncForceTrue_AndReturnsTrue()
    {
        _syncOrchestratorMock.Setup(s => s.SyncWithTrackersAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(true);

        var result = await _service.RefreshAnimeListAsync();

        Assert.True(result);
        _airingServiceMock.Verify(a => a.SyncOngoingEpisodesAsync(true, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAnimeListAsync_WhenAiringSyncThrows_StillReturnsTrue()
    {
        _syncOrchestratorMock.Setup(s => s.SyncWithTrackersAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(true);
        _airingServiceMock.Setup(a => a.SyncOngoingEpisodesAsync(true, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AniList down"));

        var result = await _service.RefreshAnimeListAsync();

        Assert.True(result);
        _airingServiceMock.Verify(a => a.SyncOngoingEpisodesAsync(true, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshMangaListAsync_DelegatesToOrchestrator()
    {
        _syncOrchestratorMock.Setup(s => s.SyncMangaWithTrackersAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(true);

        var result = await _service.RefreshMangaListAsync();

        Assert.True(result);
        _syncOrchestratorMock.Verify(s => s.SyncMangaWithTrackersAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>(), false), Times.Once);
    }

    [Fact]
    public void IsRefreshing_ReflectsOrchestratorIsSyncing()
    {
        _syncOrchestratorMock.Setup(s => s.IsSyncing).Returns(true);
        Assert.True(_service.IsRefreshing);

        _syncOrchestratorMock.Setup(s => s.IsSyncing).Returns(false);
        Assert.False(_service.IsRefreshing);
    }
}
