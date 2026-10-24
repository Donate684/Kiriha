using System;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Models;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class ScoreMenuTests
{
    [Theory]
    [InlineData(null, "-")]
    [InlineData("", "-")]
    [InlineData("-", "-")]
    [InlineData("0", "-")]
    [InlineData("-1", "-")]
    public void RatingHelper_EmptyOrZeroScore_ReturnsMinusOption(string? input, string expectedDisplay)
    {
        var option = RatingHelper.GetRatingOption(input);

        Assert.Equal("-", option.Value);
        Assert.Equal(expectedDisplay, option.Display);
    }

    [Fact]
    public void RatingHelper_ValidScore_ReturnsFormattedOption()
    {
        var option = RatingHelper.GetRatingOption("10");

        Assert.Equal("10", option.Value);
        Assert.Contains("10", option.Display);
    }

    [Fact]
    public async Task AnimeProgressService_SetScoreZero_ClearsScoreToDash()
    {
        var animeRepoMock = new Mock<IAnimeRepository>();
        var userAnimeRepoMock = new Mock<IUserAnimeRepository>();
        var syncManagerMock = new Mock<ISyncManager>();
        var historyServiceMock = new Mock<IHistoryService>();
        var uiDispatcherMock = new Mock<IUiDispatcher>();

        uiDispatcherMock
            .Setup(d => d.InvokeAsync(It.IsAny<Action>()))
            .Callback<Action>(a => a())
            .Returns(Task.CompletedTask);

        var anime = new AnimeEntity
        {
            Id = 123,
            Title = "Test Anime",
            Score = "8"
        };

        var service = new AnimeProgressService(
            animeRepoMock.Object,
            userAnimeRepoMock.Object,
            syncManagerMock.Object,
            historyServiceMock.Object,
            uiDispatcherMock.Object);

        await service.SetScoreAsync(anime, 0);

        Assert.Equal("-", anime.Score);
        userAnimeRepoMock.Verify(r => r.UpdateScoreAsync(anime, "-"), Times.Once);
        syncManagerMock.Verify(s => s.EnqueueUpdateAsync(123, anime.Progress, null, 0, null), Times.Once);
    }

    [Fact]
    public async Task AnimeProgressService_SetScorePositive_SetsNumericScore()
    {
        var animeRepoMock = new Mock<IAnimeRepository>();
        var userAnimeRepoMock = new Mock<IUserAnimeRepository>();
        var syncManagerMock = new Mock<ISyncManager>();
        var historyServiceMock = new Mock<IHistoryService>();
        var uiDispatcherMock = new Mock<IUiDispatcher>();

        uiDispatcherMock
            .Setup(d => d.InvokeAsync(It.IsAny<Action>()))
            .Callback<Action>(a => a())
            .Returns(Task.CompletedTask);

        var anime = new AnimeEntity
        {
            Id = 456,
            Title = "Test Anime 2",
            Score = "-"
        };

        var service = new AnimeProgressService(
            animeRepoMock.Object,
            userAnimeRepoMock.Object,
            syncManagerMock.Object,
            historyServiceMock.Object,
            uiDispatcherMock.Object);

        await service.SetScoreAsync(anime, 9);

        Assert.Equal("9", anime.Score);
        userAnimeRepoMock.Verify(r => r.UpdateScoreAsync(anime, "9"), Times.Once);
        syncManagerMock.Verify(s => s.EnqueueUpdateAsync(456, anime.Progress, null, 9, null), Times.Once);
    }
}
