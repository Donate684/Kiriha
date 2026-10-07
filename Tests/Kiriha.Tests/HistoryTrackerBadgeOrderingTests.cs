using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.ViewModels.History;
using Moq;

namespace Kiriha.Tests;

public class HistoryTrackerBadgeOrderingTests
{
    private readonly AppSettings _settings;
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly Mock<ILocalizer> _mockLocalizer;

    public HistoryTrackerBadgeOrderingTests()
    {
        _settings = new AppSettings();
        _mockSettingsService = new Mock<ISettingsService>();
        _mockSettingsService.Setup(s => s.Current).Returns(_settings);

        _mockLocalizer = new Mock<ILocalizer>();
        _mockLocalizer.Setup(l => l.GetLoc(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns<string, object[]>((k, a) => $"{k}:{string.Join(',', a)}");
    }

    private HistoryViewModel CreateViewModel(IEnumerable<ITrackerService>? trackers = null)
    {
        return new HistoryViewModel(
            new Mock<Kiriha.Services.Data.Core.HistoryService>(Mock.Of<Kiriha.Core.Abstractions.Repositories.IHistoryRepository>()).Object,
            null!,
            Mock.Of<Kiriha.Core.Abstractions.Repositories.IAnimeRepository>(),
            null!,
            null!,
            _mockLocalizer.Object,
            trackers: trackers,
            settingsService: _mockSettingsService.Object);
    }

    private Mock<ITrackerService> CreateTrackerMock(string name, string trackerId, bool isEnabled)
    {
        var mock = new Mock<ITrackerService>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.TrackerId).Returns(trackerId);
        mock.Setup(t => t.IsEnabled).Returns(isEnabled);
        return mock;
    }

    [Fact]
    public void GetOrderedActiveTrackers_WhenPrimaryIsMal_ReturnsMalFirstThenAniListThenShiki()
    {
        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.Mal;
        _settings.Api.ShikiMirror = ShikiMirror.Net;

        var mal = CreateTrackerMock(TrackerConstants.Names.Mal, TrackerConstants.Ids.Mal, true);
        var ani = CreateTrackerMock(TrackerConstants.Names.AniList, TrackerConstants.Ids.AniList, true);
        var shikiFork = CreateTrackerMock(TrackerConstants.Names.ShikiFork, TrackerConstants.Ids.ShikiFork, true);
        var shikiOrig = CreateTrackerMock(TrackerConstants.Names.ShikiOrig, TrackerConstants.Ids.ShikiOrig, false);

        var vm = CreateViewModel(new[] { mal.Object, ani.Object, shikiFork.Object, shikiOrig.Object });
        var ordered = vm.GetOrderedActiveTrackers();

        Assert.Equal(3, ordered.Count);
        Assert.Equal(TrackerConstants.Names.Mal, ordered[0]);
        Assert.Equal(TrackerConstants.Names.AniList, ordered[1]);
        Assert.Equal(TrackerConstants.Names.ShikiFork, ordered[2]);
    }

    [Fact]
    public void GetOrderedActiveTrackers_WhenPrimaryIsAniList_ReturnsAniListFirstThenShikiThenMal()
    {
        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.AniList;
        _settings.Api.ShikiMirror = ShikiMirror.Net;

        var mal = CreateTrackerMock(TrackerConstants.Names.Mal, TrackerConstants.Ids.Mal, true);
        var ani = CreateTrackerMock(TrackerConstants.Names.AniList, TrackerConstants.Ids.AniList, true);
        var shikiFork = CreateTrackerMock(TrackerConstants.Names.ShikiFork, TrackerConstants.Ids.ShikiFork, true);

        var vm = CreateViewModel(new[] { mal.Object, ani.Object, shikiFork.Object });
        var ordered = vm.GetOrderedActiveTrackers();

        Assert.Equal(3, ordered.Count);
        Assert.Equal(TrackerConstants.Names.AniList, ordered[0]);
        Assert.Equal(TrackerConstants.Names.ShikiFork, ordered[1]);
        Assert.Equal(TrackerConstants.Names.Mal, ordered[2]);
    }

    [Fact]
    public void GetOrderedActiveTrackers_WhenPrimaryIsShikiFork_ReturnsShikiForkFirstThenAniListThenMal()
    {
        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.ShikiFork;
        _settings.Api.ShikiMirror = ShikiMirror.Net;

        var mal = CreateTrackerMock(TrackerConstants.Names.Mal, TrackerConstants.Ids.Mal, true);
        var ani = CreateTrackerMock(TrackerConstants.Names.AniList, TrackerConstants.Ids.AniList, true);
        var shikiFork = CreateTrackerMock(TrackerConstants.Names.ShikiFork, TrackerConstants.Ids.ShikiFork, true);

        var vm = CreateViewModel(new[] { mal.Object, ani.Object, shikiFork.Object });
        var ordered = vm.GetOrderedActiveTrackers();

        Assert.Equal(3, ordered.Count);
        Assert.Equal(TrackerConstants.Names.ShikiFork, ordered[0]);
        Assert.Equal(TrackerConstants.Names.AniList, ordered[1]);
        Assert.Equal(TrackerConstants.Names.Mal, ordered[2]);
    }

    [Fact]
    public void GetOrderedActiveTrackers_WhenPrimaryIsShikiOrig_ReturnsShikiOrigFirstThenAniListThenMal()
    {
        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.ShikiOrig;
        _settings.Api.ShikiMirror = ShikiMirror.One;

        var mal = CreateTrackerMock(TrackerConstants.Names.Mal, TrackerConstants.Ids.Mal, true);
        var ani = CreateTrackerMock(TrackerConstants.Names.AniList, TrackerConstants.Ids.AniList, true);
        var shikiOrig = CreateTrackerMock(TrackerConstants.Names.ShikiOrig, TrackerConstants.Ids.ShikiOrig, true);

        var vm = CreateViewModel(new[] { mal.Object, ani.Object, shikiOrig.Object });
        var ordered = vm.GetOrderedActiveTrackers();

        Assert.Equal(3, ordered.Count);
        Assert.Equal(TrackerConstants.Names.ShikiOrig, ordered[0]);
        Assert.Equal(TrackerConstants.Names.AniList, ordered[1]);
        Assert.Equal(TrackerConstants.Names.Mal, ordered[2]);
    }

    [Fact]
    public void GetOrderedActiveTrackers_OnlyShowsActiveShikimoriMirror()
    {
        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.Mal;
        _settings.Api.ShikiMirror = ShikiMirror.Net; // Fork active

        var mal = CreateTrackerMock(TrackerConstants.Names.Mal, TrackerConstants.Ids.Mal, true);
        var shikiFork = CreateTrackerMock(TrackerConstants.Names.ShikiFork, TrackerConstants.Ids.ShikiFork, true);
        var shikiOrig = CreateTrackerMock(TrackerConstants.Names.ShikiOrig, TrackerConstants.Ids.ShikiOrig, true);

        var vm = CreateViewModel(new[] { mal.Object, shikiFork.Object, shikiOrig.Object });
        var ordered = vm.GetOrderedActiveTrackers();

        Assert.Equal(2, ordered.Count);
        Assert.Equal(TrackerConstants.Names.Mal, ordered[0]);
        Assert.Equal(TrackerConstants.Names.ShikiFork, ordered[1]);
        Assert.DoesNotContain(TrackerConstants.Names.ShikiOrig, ordered);
    }

    [Fact]
    public void GetOrderedActiveTrackers_ExcludesUnloggedTrackers()
    {
        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.Mal;
        _settings.Api.ShikiMirror = ShikiMirror.Net;

        var mal = CreateTrackerMock(TrackerConstants.Names.Mal, TrackerConstants.Ids.Mal, true);
        var ani = CreateTrackerMock(TrackerConstants.Names.AniList, TrackerConstants.Ids.AniList, false);
        var shikiFork = CreateTrackerMock(TrackerConstants.Names.ShikiFork, TrackerConstants.Ids.ShikiFork, false);

        var vm = CreateViewModel(new[] { mal.Object, ani.Object, shikiFork.Object });
        var ordered = vm.GetOrderedActiveTrackers();

        Assert.Single(ordered);
        Assert.Equal(TrackerConstants.Names.Mal, ordered[0]);
    }

    [Fact]
    public void GetOrderedActiveTrackers_WhenNoAccountsLoggedIn_ReturnsEmpty()
    {
        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.Mal;
        _settings.Api.ShikiMirror = ShikiMirror.Net;

        var mal = CreateTrackerMock(TrackerConstants.Names.Mal, TrackerConstants.Ids.Mal, false);
        var ani = CreateTrackerMock(TrackerConstants.Names.AniList, TrackerConstants.Ids.AniList, false);
        var shikiFork = CreateTrackerMock(TrackerConstants.Names.ShikiFork, TrackerConstants.Ids.ShikiFork, false);

        var vm = CreateViewModel(new[] { mal.Object, ani.Object, shikiFork.Object });
        var ordered = vm.GetOrderedActiveTrackers();

        Assert.Empty(ordered);
    }

    [Fact]
    public void HistoryEntryVm_LoadTrackerStatuses_WithOrderedTrackers_NeverDuplicatesShikimori()
    {
        var entry = new HistoryEntryVm(_mockLocalizer.Object)
        {
            AnimeId = 1,
            ActionType = 1,
            EpisodeFrom = 4,
            EpisodeTo = 4
        };

        var statuses = new Dictionary<string, TrackerSyncInfo>
        {
            ["Shikimori"] = new() { State = TrackerSyncState.NotSynced },
            ["Shikimori (Fork)"] = new() { State = TrackerSyncState.Success },
            ["MyAnimeList"] = new() { State = TrackerSyncState.Success },
            ["AniList"] = new() { State = TrackerSyncState.Success }
        };

        var orderedTrackers = new List<string>
        {
            TrackerConstants.Names.Mal,
            TrackerConstants.Names.AniList,
            TrackerConstants.Names.ShikiFork
        };

        entry.LoadTrackerStatuses(statuses, orderedTrackers: orderedTrackers);

        Assert.Equal(3, entry.TrackerBadges.Count);
        Assert.Equal("MAL", entry.TrackerBadges[0].DisplayName);
        Assert.Equal(TrackerConstants.Names.Mal, entry.TrackerBadges[0].TrackerName);
        Assert.Equal(TrackerSyncState.Success, entry.TrackerBadges[0].State);

        Assert.Equal("AniList", entry.TrackerBadges[1].DisplayName);
        Assert.Equal(TrackerConstants.Names.AniList, entry.TrackerBadges[1].TrackerName);
        Assert.Equal(TrackerSyncState.Success, entry.TrackerBadges[1].State);

        Assert.Equal("Shikimori (Fork)", entry.TrackerBadges[2].DisplayName);
        Assert.Equal(TrackerConstants.Names.ShikiFork, entry.TrackerBadges[2].TrackerName);
        Assert.Equal(TrackerSyncState.Success, entry.TrackerBadges[2].State);

        // Crucial: ensure no "Shiki" badge was added
        Assert.DoesNotContain(entry.TrackerBadges, b => b.DisplayName == "Shiki");
    }

    [Fact]
    public void HistoryEntryVm_LoadTrackerStatuses_ResolvesHistoricalAliasSyncInfo()
    {
        var entry = new HistoryEntryVm(_mockLocalizer.Object)
        {
            AnimeId = 1,
            ActionType = 1,
            EpisodeFrom = 4,
            EpisodeTo = 4
        };

        // Saved historically under old generic key "Shikimori"
        var statuses = new Dictionary<string, TrackerSyncInfo>
        {
            ["Shikimori"] = new() { State = TrackerSyncState.Success }
        };

        var orderedTrackers = new List<string>
        {
            TrackerConstants.Names.ShikiFork
        };

        entry.LoadTrackerStatuses(statuses, orderedTrackers: orderedTrackers);

        Assert.Single(entry.TrackerBadges);
        var badge = entry.TrackerBadges[0];
        Assert.Equal("Shikimori (Fork)", badge.DisplayName);
        Assert.Equal(TrackerSyncState.Success, badge.State);
    }

    [Fact]
    public void HistoryEntryVm_RevertedAction_CanTrackIsTrue_AndShowsEpisodeLabelAndBadges()
    {
        var entry = new HistoryEntryVm(_mockLocalizer.Object)
        {
            AnimeId = 42,
            ActionType = 2, // Reverted
            EpisodeFrom = 0,
            EpisodeTo = 0
        };

        Assert.True(entry.CanTrack);
        Assert.Equal("history.episode_single:0", entry.EpisodeLabel);

        var statuses = new Dictionary<string, TrackerSyncInfo>
        {
            [TrackerConstants.Names.Mal] = new() { State = TrackerSyncState.Success }
        };

        entry.LoadTrackerStatuses(statuses, orderedTrackers: new[] { TrackerConstants.Names.Mal });

        Assert.True(entry.HasTrackerBadges);
        Assert.Single(entry.TrackerBadges);
        Assert.Equal(TrackerConstants.Names.Mal, entry.TrackerBadges[0].TrackerName);
        Assert.Equal(TrackerSyncState.Success, entry.TrackerBadges[0].State);
    }
}
