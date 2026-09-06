using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Navigation;
using Kiriha.Core.Tracking.Sync;
using Kiriha.ViewModels.Main;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class SmartTransitionsTests
{
    [Fact]
    public async Task AnimeProgressService_ConfirmRewatchAsync_SetsRewatchingAndSyncs()
    {
        // Arrange
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockSyncManager = new Mock<ISyncManager>();
        var mockHistoryService = new Mock<IHistoryService>();
        var mockUiDispatcher = new Mock<IUiDispatcher>();

        mockUiDispatcher
            .Setup(x => x.InvokeAsync(It.IsAny<System.Action>()))
            .Returns<System.Action>(a => { a(); return Task.CompletedTask; });

        var progressService = new AnimeProgressService(
            mockAnimeRepo.Object,
            mockUserRepo.Object,
            mockSyncManager.Object,
            mockHistoryService.Object,
            mockUiDispatcher.Object);

        var anime = new AnimeEntity
        {
            Id = 42,
            Title = "Fullmetal Alchemist",
            Status = UserAnimeStatus.Completed,
            Progress = 64,
            TotalEpisodes = 64,
            IsRewatching = false,
            RewatchCount = 0
        };

        // Act
        await progressService.ConfirmRewatchAsync(anime, 1);

        // Assert
        Assert.True(anime.IsRewatching);
        Assert.Equal(1, anime.RewatchCount);
        Assert.Equal(1, anime.Progress);
        Assert.Equal(UserAnimeStatus.Watching, anime.Status);

        mockUserRepo.Verify(x => x.UpdateProgressAsync(anime, 1, UserAnimeStatus.Watching), Times.Once);
        mockSyncManager.Verify(x => x.EnqueueFullUpdateAsync(anime), Times.Once);
        mockHistoryService.Verify(x => x.AddEntry(anime.Id, anime.Title, anime.RussianTitle, 1, "Rewatching", null), Times.Once);
    }

    [Fact]
    public async Task MainWindowViewModel_SubmitQuickRatingInt_SetsScoreAndCloses()
    {
        // Arrange
        var mockVmFactory = new Mock<IViewModelFactory>();
        var mockSettingsService = new Mock<ISettingsService>();
        var mockProgressService = new Mock<IProgressUpdateService>();
        var mockLocalizer = new Mock<ILocalizer>();

        mockSettingsService.Setup(x => x.Current).Returns(new AppSettings());

        var vm = new MainWindowViewModel(
            mockVmFactory.Object,
            mockSettingsService.Object,
            mockProgressService.Object,
            mockLocalizer.Object);

        var anime = new AnimeEntity
        {
            Id = 100,
            Title = "Steins;Gate",
            Status = UserAnimeStatus.Completed,
            Score = "-"
        };

        vm.QuickRatingAnime = anime;
        vm.IsQuickRatingVisible = true;

        // Act
        await vm.SubmitQuickRatingIntCommand.ExecuteAsync(9);

        // Assert
        mockProgressService.Verify(x => x.SetScoreAsync(anime, 9), Times.Once);
        Assert.False(vm.IsQuickRatingVisible);
        Assert.Null(vm.QuickRatingAnime);
        Assert.Equal(9, vm.SelectedRatingScore);
    }

    [Fact]
    public async Task MainWindowViewModel_ConfirmRewatch_CallsProgressServiceAndCloses()
    {
        // Arrange
        var mockVmFactory = new Mock<IViewModelFactory>();
        var mockSettingsService = new Mock<ISettingsService>();
        var mockProgressService = new Mock<IProgressUpdateService>();
        var mockLocalizer = new Mock<ILocalizer>();

        mockSettingsService.Setup(x => x.Current).Returns(new AppSettings());

        var vm = new MainWindowViewModel(
            mockVmFactory.Object,
            mockSettingsService.Object,
            mockProgressService.Object,
            mockLocalizer.Object);

        var anime = new AnimeEntity
        {
            Id = 101,
            Title = "Clannad",
            Status = UserAnimeStatus.Completed,
            Progress = 24
        };

        vm.RewatchPromptAnime = anime;
        vm.RewatchEpisode = 1;
        vm.IsRewatchPromptVisible = true;

        // Act
        await vm.ConfirmRewatchCommand.ExecuteAsync(null);

        // Assert
        mockProgressService.Verify(x => x.ConfirmRewatchAsync(anime, 1), Times.Once);
        Assert.False(vm.IsRewatchPromptVisible);
        Assert.Null(vm.RewatchPromptAnime);
    }

    [Fact]
    public void MainWindowViewModel_DismissRewatchPrompt_ClosesWithoutUpdating()
    {
        // Arrange
        var mockVmFactory = new Mock<IViewModelFactory>();
        var mockSettingsService = new Mock<ISettingsService>();
        var mockProgressService = new Mock<IProgressUpdateService>();
        var mockLocalizer = new Mock<ILocalizer>();

        mockSettingsService.Setup(x => x.Current).Returns(new AppSettings());

        var vm = new MainWindowViewModel(
            mockVmFactory.Object,
            mockSettingsService.Object,
            mockProgressService.Object,
            mockLocalizer.Object);

        var anime = new AnimeEntity { Id = 102, Title = "Toradora" };
        vm.RewatchPromptAnime = anime;
        vm.IsRewatchPromptVisible = true;

        // Act
        vm.DismissRewatchPromptCommand.Execute(null);

        // Assert
        Assert.False(vm.IsRewatchPromptVisible);
        Assert.Null(vm.RewatchPromptAnime);
        mockProgressService.Verify(x => x.ConfirmRewatchAsync(It.IsAny<AnimeEntity>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AnimeProgressService_UpdateProgressAsync_AutoSetsDateStarted_WhenWatching()
    {
        // Arrange
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockSyncManager = new Mock<ISyncManager>();
        var mockHistoryService = new Mock<IHistoryService>();
        var mockUiDispatcher = new Mock<IUiDispatcher>();

        mockUiDispatcher
            .Setup(x => x.InvokeAsync(It.IsAny<System.Action>()))
            .Returns<System.Action>(a => { a(); return Task.CompletedTask; });

        var progressService = new AnimeProgressService(
            mockAnimeRepo.Object,
            mockUserRepo.Object,
            mockSyncManager.Object,
            mockHistoryService.Object,
            mockUiDispatcher.Object);

        var anime = new AnimeEntity
        {
            Id = 50,
            Title = "Sousou no Frieren",
            Status = UserAnimeStatus.PlanToWatch,
            Progress = 0,
            DateStarted = null,
            DateCompleted = null
        };

        // Act
        await progressService.UpdateProgressAsync(anime, 1, UserAnimeStatus.Watching);

        // Assert - DateStarted is automatically set to Today, DateCompleted remains null
        Assert.Equal(System.DateTime.Today, anime.DateStarted);
        Assert.Null(anime.DateCompleted);
        Assert.Equal(UserAnimeStatus.Watching, anime.Status);
        Assert.Equal(1, anime.Progress);
    }

    [Fact]
    public async Task AnimeProgressService_UpdateProgressAsync_AutoSetsDateCompleted_WhenCompleted()
    {
        // Arrange
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockSyncManager = new Mock<ISyncManager>();
        var mockHistoryService = new Mock<IHistoryService>();
        var mockUiDispatcher = new Mock<IUiDispatcher>();

        mockUiDispatcher
            .Setup(x => x.InvokeAsync(It.IsAny<System.Action>()))
            .Returns<System.Action>(a => { a(); return Task.CompletedTask; });

        var progressService = new AnimeProgressService(
            mockAnimeRepo.Object,
            mockUserRepo.Object,
            mockSyncManager.Object,
            mockHistoryService.Object,
            mockUiDispatcher.Object);

        var anime = new AnimeEntity
        {
            Id = 51,
            Title = "Bocchi the Rock!",
            Status = UserAnimeStatus.Watching,
            Progress = 11,
            TotalEpisodes = 12,
            DateStarted = new System.DateTime(2026, 8, 1),
            DateCompleted = null
        };

        // Act
        await progressService.UpdateProgressAsync(anime, 12, UserAnimeStatus.Completed);

        // Assert - DateCompleted is automatically set to Today, original DateStarted is preserved
        Assert.Equal(System.DateTime.Today, anime.DateCompleted);
        Assert.Equal(new System.DateTime(2026, 8, 1), anime.DateStarted);
        Assert.Equal(UserAnimeStatus.Completed, anime.Status);
        Assert.Equal(12, anime.Progress);
    }

    [Fact]
    public void AnimeEditViewModel_DateCommands_SetAndClearCorrectly()
    {
        // Arrange
        var anime = new AnimeEntity
        {
            Id = 52,
            Title = "Cyberpunk: Edgerunners",
            Status = UserAnimeStatus.Watching,
            DateStarted = null,
            DateCompleted = null
        };

        var mockProgressService = new Mock<IProgressUpdateService>();
        var mockSyncManager = new Mock<ISyncManager>();
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        var mockHistoryService = new Mock<IHistoryService>();

        var vm = new Kiriha.ViewModels.AnimeDetails.AnimeEditViewModel(
            anime,
            anime,
            mockSyncManager.Object,
            mockAnimeRepo.Object,
            mockProgressService.Object,
            mockHistoryService.Object);

        // Act & Assert Set Start Date
        vm.SetStartDateToTodayCommand.Execute(null);
        Assert.Equal(System.DateTime.Today, anime.DateStarted);

        // Act & Assert Set End Date
        vm.SetEndDateToTodayCommand.Execute(null);
        Assert.Equal(System.DateTime.Today, anime.DateCompleted);

        // Act & Assert Clear Start Date
        vm.ClearStartDateCommand.Execute(null);
        Assert.Null(anime.DateStarted);

        // Act & Assert Clear End Date
        vm.ClearEndDateCommand.Execute(null);
        Assert.Null(anime.DateCompleted);
    }
}
