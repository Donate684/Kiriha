using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class AnimeListActionServiceTests
{
    private readonly Mock<IAnimeRepository> _animeRepoMock = new();
    private readonly Mock<ISyncManager> _syncManagerMock = new();
    private readonly Mock<IProgressUpdateService> _progressServiceMock = new();
    private readonly Mock<IHistoryService> _historyServiceMock = new();
    private readonly Mock<IUiDispatcher> _uiDispatcherMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<ITrackerService> _trackerMock = new();
    private readonly Mock<IAiringInfoService> _airingServiceMock = new();

    public AnimeListActionServiceTests()
    {
        _uiDispatcherMock
            .Setup(d => d.InvokeAsync(It.IsAny<Action>()))
            .Callback<Action>(action => action())
            .Returns(Task.CompletedTask);

        _trackerMock.SetupGet(t => t.IsEnabled).Returns(true);
        _trackerMock.SetupGet(t => t.TrackerId).Returns("mal");
    }

    private AnimeListActionService CreateService(bool enableTracker = true)
    {
        _trackerMock.SetupGet(t => t.IsEnabled).Returns(enableTracker);
        var trackers = new List<ITrackerService> { _trackerMock.Object };

        return new AnimeListActionService(
            _animeRepoMock.Object,
            _syncManagerMock.Object,
            _progressServiceMock.Object,
            _historyServiceMock.Object,
            _uiDispatcherMock.Object,
            trackers,
            settingsService: null,
            notificationService: _notificationServiceMock.Object,
            airingInfoService: _airingServiceMock.Object);
    }

    [Fact]
    public async Task AddToListAsync_ValidAnime_SetsStatus_SavesRepo_AndEnqueuesSync()
    {
        var service = CreateService(enableTracker: true);
        var item = new AnimeEntity
        {
            Id = 100,
            Title = "Frieren: Beyond Journey's End",
            MediaKind = MediaKind.Anime,
            Status = UserAnimeStatus.None
        };

        var result = await service.AddToListAsync(item, UserAnimeStatus.Watching);

        Assert.True(result.Success);
        Assert.True(result.HasActiveTrackers);
        Assert.Equal(UserAnimeStatus.Watching, item.Status);
        Assert.Equal(DateTime.Today, item.DateStarted);

        _animeRepoMock.Verify(r => r.AddOrUpdateAnimeAsync(item), Times.Once);
        _historyServiceMock.Verify(h => h.AddEntryAsync(
            item.Id,
            item.Title,
            item.RussianTitle,
            It.IsAny<int>(),
            "AddedToList",
            null,
            item.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);
        _syncManagerMock.Verify(s => s.EnqueueFullUpdateAsync(item), Times.Once);
    }

    [Fact]
    public async Task AddToListAsync_WithProgress_WritesWatchedHistory()
    {
        var service = CreateService(enableTracker: true);
        var item = new AnimeEntity
        {
            Id = 101,
            Title = "Attack on Titan",
            MediaKind = MediaKind.Anime,
            Status = UserAnimeStatus.None
        };

        var result = await service.AddToListAsync(item, UserAnimeStatus.Watching, progress: 3);

        Assert.True(result.Success);
        Assert.Equal(3, item.Progress);
        _historyServiceMock.Verify(h => h.AddEntryAsync(
            item.Id,
            item.Title,
            item.RussianTitle,
            3,
            "Watched",
            null,
            item.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddToListAsync_InvalidEntity_RejectsWithoutTouchingRepo()
    {
        var service = CreateService();
        var item = new AnimeEntity { Id = 0, Title = "" };

        var result = await service.AddToListAsync(item, UserAnimeStatus.Watching);

        Assert.False(result.Success);
        _animeRepoMock.Verify(r => r.AddOrUpdateAnimeAsync(It.IsAny<AnimeEntity>()), Times.Never);
        _syncManagerMock.Verify(s => s.EnqueueFullUpdateAsync(It.IsAny<AnimeEntity>()), Times.Never);
    }

    [Fact]
    public async Task AddToListAsync_StatusNone_Rejects()
    {
        var service = CreateService();
        var item = new AnimeEntity { Id = 1, Title = "Test Title" };

        var result = await service.AddToListAsync(item, UserAnimeStatus.None);

        Assert.False(result.Success);
        _animeRepoMock.Verify(r => r.AddOrUpdateAnimeAsync(It.IsAny<AnimeEntity>()), Times.Never);
    }

    [Fact]
    public async Task AddToListAsync_NotYetAired_RejectsWatchingStatus()
    {
        var service = CreateService();
        var item = new AnimeEntity
        {
            Id = 200,
            Title = "Upcoming Anime",
            StatusDetailed = AppConstants.AiringStatus.NotYetAired
        };

        var result = await service.AddToListAsync(item, UserAnimeStatus.Watching);

        Assert.False(result.Success);
        _animeRepoMock.Verify(r => r.AddOrUpdateAnimeAsync(It.IsAny<AnimeEntity>()), Times.Never);
    }

    [Fact]
    public async Task AddToListAsync_NoActiveTrackers_SavesLocallyAndNotifies()
    {
        var service = CreateService(enableTracker: false);
        var item = new AnimeEntity
        {
            Id = 300,
            Title = "Offline Item",
            MediaKind = MediaKind.Anime,
            Status = UserAnimeStatus.None
        };

        var result = await service.AddToListAsync(item, UserAnimeStatus.PlanToWatch);

        Assert.True(result.Success);
        Assert.False(result.HasActiveTrackers);
        _animeRepoMock.Verify(r => r.AddOrUpdateAnimeAsync(item), Times.Once);
        _syncManagerMock.Verify(s => s.EnqueueFullUpdateAsync(It.IsAny<AnimeEntity>()), Times.Never);
        _notificationServiceMock.Verify(n => n.NotifySyncFailed(
            item.Title,
            "Trackers",
            It.IsAny<string>(),
            false), Times.Once);
    }

    [Fact]
    public async Task SaveAnimeAsync_Completed_SetsCompletionDatesAndWritesHistory()
    {
        var service = CreateService(enableTracker: true);
        var original = new AnimeEntity
        {
            Id = 400,
            Title = "Finished Anime",
            Status = UserAnimeStatus.Watching
        };
        var updated = original.Clone();
        updated.Status = UserAnimeStatus.Completed;

        var result = await service.SaveAnimeAsync(original, updated);

        Assert.True(result.Success);
        Assert.Equal(UserAnimeStatus.Completed, original.Status);
        Assert.Equal(DateTime.Today, original.DateCompleted);

        _historyServiceMock.Verify(h => h.AddEntryAsync(
            original.Id,
            original.Title,
            original.RussianTitle,
            It.IsAny<int>(),
            "Completed",
            null,
            original.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);
        _syncManagerMock.Verify(s => s.EnqueueFullUpdateAsync(original), Times.Once);
    }

    [Fact]
    public async Task SaveAnimeAsync_DroppedAnime_ProgressIncremented_WritesWatchedHistory()
    {
        var service = CreateService(enableTracker: true);
        var original = new AnimeEntity
        {
            Id = 401,
            Title = "Aoki Denshou",
            RussianTitle = "Лазурные сказания",
            Status = UserAnimeStatus.Dropped,
            Progress = 0,
            MainPictureUrl = "https://example.com/poster.jpg"
        };
        var updated = original.Clone();
        updated.Progress = 1;

        var result = await service.SaveAnimeAsync(original, updated);

        Assert.True(result.Success);
        Assert.Equal(1, original.Progress);
        Assert.Equal(UserAnimeStatus.Dropped, original.Status);

        _historyServiceMock.Verify(h => h.AddEntryAsync(
            original.Id,
            original.Title,
            original.RussianTitle,
            1,
            "Watched",
            null,
            original.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);
        _syncManagerMock.Verify(s => s.EnqueueFullUpdateAsync(original), Times.Once);
    }

    [Fact]
    public async Task SaveAnimeAsync_DroppedAnime_ProgressDecremented_WritesRevertedHistory()
    {
        var service = CreateService(enableTracker: true);
        var original = new AnimeEntity
        {
            Id = 402,
            Title = "Dropped Anime Revert",
            Status = UserAnimeStatus.Dropped,
            Progress = 3
        };
        var updated = original.Clone();
        updated.Progress = 2;

        var result = await service.SaveAnimeAsync(original, updated);

        Assert.True(result.Success);
        Assert.Equal(2, original.Progress);

        _historyServiceMock.Verify(h => h.AddEntryAsync(
            original.Id,
            original.Title,
            original.RussianTitle,
            2,
            "Reverted",
            null,
            original.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAnimeAsync_Manga_ChaptersReadIncremented_WritesReadHistory()
    {
        var service = CreateService(enableTracker: true);
        var original = new AnimeEntity
        {
            Id = 403,
            Title = "Dropped Manga",
            MediaKind = MediaKind.Manga,
            Status = UserAnimeStatus.Dropped,
            ChaptersRead = 5
        };
        var updated = original.Clone();
        updated.ChaptersRead = 6;

        var result = await service.SaveAnimeAsync(original, updated);

        Assert.True(result.Success);
        Assert.Equal(6, original.ChaptersRead);

        _historyServiceMock.Verify(h => h.AddEntryAsync(
            original.Id,
            original.Title,
            original.RussianTitle,
            6,
            "Read",
            null,
            original.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAnimeAsync_StatusChangedToDroppedAndProgressIncremented_WritesBothWatchedAndDroppedHistory()
    {
        var service = CreateService(enableTracker: true);
        var original = new AnimeEntity
        {
            Id = 404,
            Title = "Dropped with Progress Increment",
            Status = UserAnimeStatus.Watching,
            Progress = 1
        };
        var updated = original.Clone();
        updated.Status = UserAnimeStatus.Dropped;
        updated.Progress = 2;

        var result = await service.SaveAnimeAsync(original, updated);

        Assert.True(result.Success);
        Assert.Equal(UserAnimeStatus.Dropped, original.Status);
        Assert.Equal(2, original.Progress);

        _historyServiceMock.Verify(h => h.AddEntryAsync(
            original.Id,
            original.Title,
            original.RussianTitle,
            2,
            "Watched",
            null,
            original.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);

        _historyServiceMock.Verify(h => h.AddEntryAsync(
            original.Id,
            original.Title,
            original.RussianTitle,
            2,
            "Dropped",
            null,
            original.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAnimeAsync_StatusChangedToWatchingWithProgress_DoesNotDuplicateWatchedHistory()
    {
        var service = CreateService(enableTracker: true);
        var original = new AnimeEntity
        {
            Id = 405,
            Title = "Start Watching Anime",
            Status = UserAnimeStatus.PlanToWatch,
            Progress = 0
        };
        var updated = original.Clone();
        updated.Status = UserAnimeStatus.Watching;
        updated.Progress = 1;

        var result = await service.SaveAnimeAsync(original, updated);

        Assert.True(result.Success);
        Assert.Equal(UserAnimeStatus.Watching, original.Status);
        Assert.Equal(1, original.Progress);

        _historyServiceMock.Verify(h => h.AddEntryAsync(
            original.Id,
            original.Title,
            original.RussianTitle,
            1,
            "Watched",
            null,
            original.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveFromListAsync_DelegatesToProgressService()
    {
        var service = CreateService();

        var result = await service.RemoveFromListAsync(500);

        Assert.True(result.Success);
        _progressServiceMock.Verify(p => p.RemoveAnimeAsync(500), Times.Once);
    }

    [Fact]
    public async Task AddToListAsync_ExistingEntityInCollection_PreservesScoreNotesAndDates()
    {
        var existingInCollection = new AnimeEntity
        {
            Id = 600,
            Title = "Steins;Gate",
            Score = "10",
            Notes = "Masterpiece",
            Progress = 12,
            DateStarted = new DateTime(2023, 1, 1),
            IsRewatching = true,
            RewatchCount = 1
        };

        var collection = new System.Collections.ObjectModel.ObservableCollection<AnimeEntity> { existingInCollection };
        _animeRepoMock.SetupGet(r => r.Collection).Returns(collection);

        var service = CreateService();
        var shallowSearchItem = new AnimeEntity
        {
            Id = 600,
            Title = "Steins;Gate",
            Score = "-",
            Notes = null,
            Progress = 0,
            DateStarted = null
        };

        var result = await service.AddToListAsync(shallowSearchItem, UserAnimeStatus.Watching);

        Assert.True(result.Success);
        Assert.Equal("10", shallowSearchItem.Score);
        Assert.Equal("Masterpiece", shallowSearchItem.Notes);
        Assert.Equal(12, shallowSearchItem.Progress);
        Assert.Equal(new DateTime(2023, 1, 1), shallowSearchItem.DateStarted);
        Assert.True(shallowSearchItem.IsRewatching);
        Assert.Equal(1, shallowSearchItem.RewatchCount);
    }

    [Fact]
    public async Task SaveAnimeAsync_MismatchedIds_Rejects()
    {
        var service = CreateService();
        var original = new AnimeEntity { Id = 701, Title = "Anime A" };
        var updated = new AnimeEntity { Id = 702, Title = "Anime B" };

        var result = await service.SaveAnimeAsync(original, updated);

        Assert.False(result.Success);
        _animeRepoMock.Verify(r => r.AddOrUpdateAnimeAsync(It.IsAny<AnimeEntity>()), Times.Never);
    }

    [Fact]
    public async Task SaveAnimeAsync_ClearingNotesAndScore_UpdatesOriginal()
    {
        var service = CreateService();
        var original = new AnimeEntity
        {
            Id = 800,
            Title = "Hunter x Hunter",
            Score = "9",
            Notes = "Loved the chimera ant arc",
            Status = UserAnimeStatus.Watching
        };
        var updated = original.Clone();
        updated.Notes = "";
        updated.Score = "-";

        var result = await service.SaveAnimeAsync(original, updated);

        Assert.True(result.Success);
        Assert.Equal("", original.Notes);
        Assert.Equal("-", original.Score);
        _animeRepoMock.Verify(r => r.AddOrUpdateAnimeAsync(original), Times.Once);
    }

    [Fact]
    public void AnimeEntity_CopyTo_CopiesMangaFieldsCorrectly()
    {
        var source = new AnimeEntity
        {
            Id = 900,
            Title = "Berserk",
            MediaKind = MediaKind.Manga,
            Chapters = 370,
            Volumes = 41,
            ChaptersRead = 150,
            VolumesRead = 18,
            LocalPosterPath = "C:/cache/berserk.jpg"
        };
        var target = new AnimeEntity
        {
            Id = 900,
            Title = "Berserk",
            MediaKind = MediaKind.Manga
        };

        source.CopyTo(target);

        Assert.Equal(370, target.Chapters);
        Assert.Equal(41, target.Volumes);
        Assert.Equal(150, target.ChaptersRead);
        Assert.Equal(18, target.VolumesRead);
    }

    [Fact]
    public async Task AddToListAsync_OngoingAnime_TriggersAiringSync()
    {
        var service = CreateService(enableTracker: true);
        var item = new AnimeEntity
        {
            Id = 101,
            Title = "Ongoing Anime",
            MediaKind = MediaKind.Anime,
            StatusDetailed = "currently_airing"
        };

        var result = await service.AddToListAsync(item, UserAnimeStatus.Watching);

        Assert.True(result.Success);
        await Task.Delay(50);
        _airingServiceMock.Verify(a => a.SyncEpisodesForAnimeAsync(item, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddToListAsync_PlanToWatch_DoesNotTriggerAiringSync()
    {
        var service = CreateService(enableTracker: true);
        var item = new AnimeEntity
        {
            Id = 102,
            Title = "Plan to Watch Anime",
            MediaKind = MediaKind.Anime,
            StatusDetailed = "currently_airing"
        };

        var result = await service.AddToListAsync(item, UserAnimeStatus.PlanToWatch);

        Assert.True(result.Success);
        await Task.Delay(50);
        _airingServiceMock.Verify(a => a.SyncEpisodesForAnimeAsync(item, It.IsAny<CancellationToken>()), Times.Never);
    }
}
