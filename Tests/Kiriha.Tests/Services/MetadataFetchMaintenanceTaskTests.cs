using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Maintenance;
using Moq;
using Xunit;

namespace Kiriha.Tests.Services;

public sealed class MetadataFetchMaintenanceTaskTests
{
    private readonly Mock<ISettingsService> _settingsMock;
    private readonly Mock<IAnimeRepository> _animeRepoMock;
    private readonly Mock<IUserAnimeRepository> _userAnimeRepoMock;
    private readonly Mock<IMetadataRepository> _metadataRepoMock;
    private readonly Mock<ShikiMetadataService> _shikiMetadataMock;
    private readonly Mock<ImageCacheService> _imageCacheMock;
    private readonly Mock<IUiDispatcher> _uiDispatcherMock;
    private readonly AppSettings _appSettings;

    public MetadataFetchMaintenanceTaskTests()
    {
        _appSettings = new AppSettings();
        _appSettings.System.EnableBackgroundMetadataFetch = true;

        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.Current).Returns(_appSettings);

        _animeRepoMock = new Mock<IAnimeRepository>();
        _animeRepoMock.Setup(r => r.InitializationTask).Returns(Task.CompletedTask);

        _userAnimeRepoMock = new Mock<IUserAnimeRepository>();
        _metadataRepoMock = new Mock<IMetadataRepository>();

        _uiDispatcherMock = new Mock<IUiDispatcher>();
        _uiDispatcherMock.Setup(d => d.InvokeAsync(It.IsAny<Action>())).Callback<Action>(a => a()).Returns(Task.CompletedTask);
        _uiDispatcherMock.Setup(d => d.Post(It.IsAny<Action>())).Callback<Action>(a => a());

        _shikiMetadataMock = new Mock<ShikiMetadataService>();
        _imageCacheMock = new Mock<ImageCacheService>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_DoesNotFetchAnyItems()
    {
        _appSettings.System.EnableBackgroundMetadataFetch = false;

        var task = new MetadataFetchMaintenanceTask(
            _settingsMock.Object,
            _animeRepoMock.Object,
            _userAnimeRepoMock.Object,
            _metadataRepoMock.Object,
            _shikiMetadataMock.Object,
            _imageCacheMock.Object,
            _uiDispatcherMock.Object);

        await task.ExecuteAsync(CancellationToken.None);

        _animeRepoMock.Verify(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>()), Times.Never);
        _shikiMetadataMock.Verify(s => s.GetOrFetchMetadataAsync(It.IsAny<int>(), It.IsAny<TimeSpan?>(), It.IsAny<Func<ShikiMetadata, Task>?>(), It.IsAny<MediaKind>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenItemMissingRussianTitle_FetchesMetadata_AndUpdatesLiveItem()
    {
        var item = new AnimeEntity
        {
            Id = 100,
            Title = "Frieren",
            RussianTitle = null,
            RussianSynopsis = null,
            Status = UserAnimeStatus.Watching,
            MediaKind = MediaKind.Anime
        };

        _animeRepoMock.Setup(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>()))
            .ReturnsAsync(new List<AnimeEntity> { item });

        _metadataRepoMock.Setup(m => m.GetAsync(100))
            .ReturnsAsync((ShikiMetadata?)null);

        _shikiMetadataMock.Setup(s => s.GetOrFetchMetadataAsync(100, null, null, MediaKind.Anime))
            .ReturnsAsync(new ShikiMetadata
            {
                Id = 100,
                Russian = "Провожающая в последний путь Фрирен",
                Description = "Описание истории Фрирен"
            });

        var task = new MetadataFetchMaintenanceTask(
            _settingsMock.Object,
            _animeRepoMock.Object,
            _userAnimeRepoMock.Object,
            _metadataRepoMock.Object,
            _shikiMetadataMock.Object,
            _imageCacheMock.Object,
            _uiDispatcherMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await task.ExecuteAsync(cts.Token);

        Assert.Equal("Провожающая в последний путь Фрирен", item.RussianTitle);
        Assert.Equal("Описание истории Фрирен", item.RussianSynopsis);

        _userAnimeRepoMock.Verify(u => u.UpdateMetadataAsync(item), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesLiveItem_MakingItSearchableInProjection()
    {
        var item = new AnimeEntity
        {
            Id = 101,
            Title = "Attack on Titan",
            RussianTitle = null,
            Status = UserAnimeStatus.Watching,
            MediaKind = MediaKind.Anime
        };

        using var projection = new AnimeCollectionProjection();
        projection.Rebuild(new[] { item });

        // Initially searching for Russian title returns nothing
        var initialResults = projection.Query(UserAnimeStatus.Watching, "Атака", false, AppConstants.Sorting.Title, MediaKind.Anime);
        Assert.Empty(initialResults);

        _animeRepoMock.Setup(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>()))
            .ReturnsAsync(new List<AnimeEntity> { item });

        _metadataRepoMock.Setup(m => m.GetAsync(101))
            .ReturnsAsync(new ShikiMetadata
            {
                Id = 101,
                Russian = "Атака титанов",
                Description = "Описание титанов"
            });

        var task = new MetadataFetchMaintenanceTask(
            _settingsMock.Object,
            _animeRepoMock.Object,
            _userAnimeRepoMock.Object,
            _metadataRepoMock.Object,
            _shikiMetadataMock.Object,
            _imageCacheMock.Object,
            _uiDispatcherMock.Object);

        await task.ExecuteAsync(CancellationToken.None);

        // Assert item was updated
        Assert.Equal("Атака титанов", item.RussianTitle);

        // Assert live search immediately finds the item without restarting
        var updatedResults = projection.Query(UserAnimeStatus.Watching, "Атака", false, AppConstants.Sorting.Title, MediaKind.Anime);
        Assert.Single(updatedResults);
        Assert.Equal(101, updatedResults[0].Id);
    }

    [Fact]
    public async Task ExecuteAsync_WhenItemAlreadyComplete_SkipsProcessing()
    {
        // Create a temporary file to act as an existing poster
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "fake image bytes");

        try
        {
            var item = new AnimeEntity
            {
                Id = 102,
                Title = "Already Localized",
                RussianTitle = "Уже переведено",
                RussianSynopsis = "Полное описание",
                MainPictureUrl = "https://example.com/poster.jpg",
                LocalPosterPath = tempFile,
                MediaKind = MediaKind.Anime
            };

            _animeRepoMock.Setup(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>()))
                .ReturnsAsync(new List<AnimeEntity> { item });

            var task = new MetadataFetchMaintenanceTask(
                _settingsMock.Object,
                _animeRepoMock.Object,
                _userAnimeRepoMock.Object,
                _metadataRepoMock.Object,
                _shikiMetadataMock.Object,
                _imageCacheMock.Object,
                _uiDispatcherMock.Object);

            await task.ExecuteAsync(CancellationToken.None);

            _metadataRepoMock.Verify(m => m.GetAsync(It.IsAny<int>()), Times.Never);
            _shikiMetadataMock.Verify(s => s.GetOrFetchMetadataAsync(It.IsAny<int>(), It.IsAny<TimeSpan?>(), It.IsAny<Func<ShikiMetadata, Task>?>(), It.IsAny<MediaKind>()), Times.Never);
            _imageCacheMock.Verify(c => c.GetLocalPathOrDownload(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
