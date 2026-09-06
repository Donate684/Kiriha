using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking;
using Kiriha.ViewModels.Analytics;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class InspectorSectionViewModelTests
{
    private readonly Mock<IAnimeRepository> _mockAnimeRepo = new();
    private readonly Mock<ISyncManager> _mockSyncManager = new();
    private readonly Mock<IDialogService> _mockDialogService = new();
    private readonly Mock<ILocalizer> _mockLocalizer = new();
    private readonly Mock<Kiriha.Core.Abstractions.Services.Tracking.IMalHistoryDeepParserService> _mockDeepParserService = new();

    public InspectorSectionViewModelTests()
    {
        _mockLocalizer.Setup(x => x.GetLoc(It.IsAny<string>())).Returns<string>(key => key);
    }

    private InspectorSectionViewModel CreateVm() =>
        new(_mockAnimeRepo.Object, _mockSyncManager.Object, _mockDialogService.Object, _mockLocalizer.Object, _mockDeepParserService.Object);

    [Fact]
    public void Refresh_DetectsMissingEndDate_WhenCompletedWithoutDate()
    {
        var vm = CreateVm();

        var items = new List<AnimeEntity>
        {
            new AnimeEntity
            {
                Id = 1,
                Title = "Sousou no Frieren",
                Status = UserAnimeStatus.Completed,
                DateCompleted = null,
                DateStarted = new DateTime(2023, 10, 1),
                TotalEpisodes = 28,
                Progress = 28,
                Score = "10"
            }
        };

        vm.Refresh(items);

        Assert.True(vm.HasIssues);
        Assert.Equal(1, vm.IssuesCount);
        Assert.Equal(1, vm.MissingDatesCount);
        Assert.Contains(vm.Issues, x => x.IssueType == ProfileIssueType.MissingEndDate);
    }

    [Fact]
    public void Refresh_DetectsMissingStartDate_WhenWatchingWithoutDate()
    {
        var vm = CreateVm();

        var items = new List<AnimeEntity>
        {
            new AnimeEntity
            {
                Id = 2,
                Title = "Bleach: TYBW",
                Status = UserAnimeStatus.Watching,
                DateStarted = null,
                Progress = 5,
                TotalEpisodes = 13
            }
        };

        vm.Refresh(items);

        Assert.True(vm.HasIssues);
        Assert.Equal(1, vm.IssuesCount);
        Assert.Equal(1, vm.MissingDatesCount);
        Assert.Contains(vm.Issues, x => x.IssueType == ProfileIssueType.MissingStartDate);
    }

    [Fact]
    public void Refresh_DetectsCompletedMismatch_WhenProgressEqualsTotalEpisodes()
    {
        var vm = CreateVm();

        var items = new List<AnimeEntity>
        {
            new AnimeEntity
            {
                Id = 3,
                Title = "Steins;Gate",
                Status = UserAnimeStatus.Watching,
                DateStarted = new DateTime(2023, 1, 1),
                Progress = 24,
                TotalEpisodes = 24
            }
        };

        vm.Refresh(items);

        Assert.True(vm.HasIssues);
        Assert.Equal(1, vm.StatusMismatchCount);
        Assert.Contains(vm.Issues, x => x.IssueType == ProfileIssueType.EpisodesFinished);
    }

    [Fact]
    public void Refresh_CalculatesHealthScoreCorrectly()
    {
        var vm = CreateVm();

        var items = new List<AnimeEntity>
        {
            // Clean item (no issues)
            new AnimeEntity
            {
                Id = 1,
                Title = "Clannad",
                Status = UserAnimeStatus.Completed,
                DateStarted = new DateTime(2022, 1, 1),
                DateCompleted = new DateTime(2022, 1, 10),
                Progress = 24,
                TotalEpisodes = 24,
                Score = "9"
            },
            // Problem item (missing end date)
            new AnimeEntity
            {
                Id = 2,
                Title = "Monster",
                Status = UserAnimeStatus.Completed,
                DateStarted = new DateTime(2022, 2, 1),
                DateCompleted = null,
                Progress = 74,
                TotalEpisodes = 74,
                Score = "10"
            }
        };

        vm.Refresh(items);

        // 1 of 2 has issues -> HealthScore should be 50%
        Assert.Equal(50, vm.HealthScore);
        Assert.True(vm.HasIssues);
    }

    [Fact]
    public async Task BatchFixCompletedDates_SetsDateCompletedToToday_AndUpdatesRepo()
    {
        var vm = CreateVm();

        var anime1 = new AnimeEntity
        {
            Id = 10,
            Title = "Baccano!",
            Status = UserAnimeStatus.Completed,
            DateStarted = new DateTime(2021, 5, 1),
            DateCompleted = null,
            Progress = 13,
            TotalEpisodes = 13,
            Score = "8"
        };

        vm.Refresh(new[] { anime1 });

        Assert.Equal(1, vm.MissingDatesCount);

        // Act
        await vm.BatchFixCompletedDates();

        // Assert
        Assert.NotNull(anime1.DateCompleted);
        Assert.Equal(DateTime.Today, anime1.DateCompleted.Value.Date);
        _mockAnimeRepo.Verify(x => x.AddOrUpdateAnimeAsync(anime1), Times.Once);
        _mockSyncManager.Verify(x => x.EnqueueFullUpdateAsync(anime1), Times.Once);
        Assert.Equal(0, vm.MissingDatesCount);
    }

    [Fact]
    public async Task BatchFixFinishedStatus_SetsStatusToCompleted_AndUpdatesRepo()
    {
        var vm = CreateVm();

        var anime = new AnimeEntity
        {
            Id = 20,
            Title = "Ping Pong The Animation",
            Status = UserAnimeStatus.Watching,
            DateStarted = new DateTime(2023, 5, 1),
            Progress = 11,
            TotalEpisodes = 11
        };

        vm.Refresh(new[] { anime });

        Assert.Equal(1, vm.StatusMismatchCount);

        // Act
        await vm.BatchFixFinishedStatus();

        // Assert
        Assert.Equal(UserAnimeStatus.Completed, anime.Status);
        Assert.NotNull(anime.DateCompleted);
        Assert.Equal(DateTime.Today, anime.DateCompleted.Value.Date);
        _mockAnimeRepo.Verify(x => x.AddOrUpdateAnimeAsync(anime), Times.Once);
        _mockSyncManager.Verify(x => x.EnqueueFullUpdateAsync(anime), Times.Once);
        Assert.Equal(0, vm.StatusMismatchCount);
    }
}
