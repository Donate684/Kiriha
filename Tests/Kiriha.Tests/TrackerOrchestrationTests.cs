using Moq;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;

namespace Kiriha.Tests;

public sealed class TrackerOrchestrationTests
{
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
            IsEnabled = true,
            IsPrimary = false,
            IsMirror = true
        });
        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
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
