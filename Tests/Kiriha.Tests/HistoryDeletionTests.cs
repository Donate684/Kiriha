using System.Collections.ObjectModel;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Core;
using Kiriha.ViewModels.History;
using Kiriha.Views.Converters;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class HistoryDeletionTests
{
    [Fact]
    public async Task RemoveAnimeAsync_RecordsDeletedInHistory_WithPosterUrl()
    {
        // Arrange
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockSyncManager = new Mock<ISyncManager>();
        var mockHistoryService = new Mock<IHistoryService>();
        var mockUiDispatcher = new Mock<IUiDispatcher>();

        var anime = new AnimeEntity
        {
            Id = 62485,
            Title = "Rent-a-Girlfriend Season 5",
            RussianTitle = "Девушка на час 5",
            Progress = 2,
            MainPictureUrl = "https://shikimori.one/system/animes/original/62485.jpg"
        };

        mockAnimeRepo.Setup(r => r.Collection).Returns(new ObservableCollection<AnimeEntity> { anime });

        var progressService = new AnimeProgressService(
            mockAnimeRepo.Object,
            mockUserRepo.Object,
            mockSyncManager.Object,
            mockHistoryService.Object,
            mockUiDispatcher.Object);

        // Act
        await progressService.RemoveAnimeAsync(anime.Id);

        // Assert
        mockHistoryService.Verify(h => h.AddEntryAsync(
            anime.Id,
            anime.Title,
            anime.RussianTitle,
            anime.Progress,
            "Deleted",
            null,
            anime.MainPictureUrl,
            It.IsAny<CancellationToken>()), Times.Once);

        mockAnimeRepo.Verify(r => r.RemoveAnimeLocalAsync(anime.Id), Times.Once);
        mockSyncManager.Verify(s => s.EnqueueRemoveAsync(anime.Id), Times.Once);
    }

    [Fact]
    public void HistoryActionConverter_Type8_ReturnsDeletedIconAndText()
    {
        var converter = new HistoryActionConverter();

        var icon = converter.Convert(8, typeof(string), "icon", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("DeleteOutline", icon);
    }

    [Fact]
    public void HistoryActionConverter_ZeroProgressEntry_ReturnsPlaylistPlusAndAddedToList()
    {
        var converter = new HistoryActionConverter();
        var mockLocalizer = new Mock<Kiriha.Core.Abstractions.Services.ILocalizer>();
        mockLocalizer.Setup(l => l.GetLoc("history.episode_single", 0)).Returns("эп. 0");

        var entry = new Kiriha.ViewModels.History.HistoryEntryVm(mockLocalizer.Object)
        {
            ActionType = 1,
            EpisodeFrom = 0,
            EpisodeTo = 0
        };

        var icon = converter.Convert(entry, typeof(string), "icon", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("PlaylistPlus", icon);
        Assert.Equal("эп. 0", entry.EpisodeLabel);
    }

    [Fact]
    public async Task HistoryViewModel_RefreshHistory_RestoresPosterForDeletedAnime_FromMetadataRepo()
    {
        // Arrange
        var mockHistoryRepo = new Mock<IHistoryRepository>();
        var rawHistory = new List<HistoryItem>
        {
            new HistoryItem
            {
                Id = 1,
                AnimeId = 62485,
                AnimeTitle = "Rent-a-Girlfriend Season 5",
                RussianTitle = "Девушка на час 5",
                ActionType = 1,
                PosterUrl = null // Simulating legacy/deleted item without stored poster
            }
        };

        mockHistoryRepo.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawHistory);

        var historyService = new HistoryService(mockHistoryRepo.Object);

        var mockAnimeRepo = new Mock<IAnimeRepository>();
        mockAnimeRepo.Setup(r => r.Collection).Returns(new ObservableCollection<AnimeEntity>()); // Empty collection, anime was deleted!

        var mockMetadataRepo = new Mock<IMetadataRepository>();
        mockMetadataRepo.Setup(m => m.GetBatchAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, ShikiMetadata>
            {
                [62485] = new ShikiMetadata
                {
                    Id = 62485,
                    PosterUrl = "https://shikimori.one/system/animes/original/62485.jpg"
                }
            });

        var mockLocalizer = new Mock<ILocalizer>();
        mockLocalizer.Setup(l => l.GetLoc(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns<string, object[]>((k, a) => k);

        var vm = new HistoryViewModel(
            historyService,
            null!,
            mockAnimeRepo.Object,
            null!,
            null!,
            mockLocalizer.Object,
            metadataRepo: mockMetadataRepo.Object);

        // Act
        await vm.RefreshHistory();

        // Assert
        Assert.Single(rawHistory);
        Assert.Equal("https://shikimori.one/system/animes/original/62485.jpg", rawHistory[0].PosterUrl);
    }
}
