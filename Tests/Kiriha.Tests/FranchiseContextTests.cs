using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Franchise;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public sealed class FranchiseContextTests
{
    [Fact]
    public void HasFranchiseBadge_False_WhenAnimeHasDirectUserStatus()
    {
        var anime = new AnimeEntity
        {
            Id = 100,
            Status = UserAnimeStatus.Watching,
            Franchise = new FranchiseContext
            {
                Relation = FranchiseRelationKind.Sequel,
                UserStatus = UserAnimeStatus.Completed,
                RelatedTitle = "Season 1"
            }
        };

        // When user has direct status (e.g. Watching), direct status badge takes precedence
        Assert.False(anime.Presentation.HasFranchiseBadge);
    }

    [Fact]
    public void HasFranchiseBadge_True_WhenAnimeNotInList_AndHasFranchiseRelation()
    {
        var anime = new AnimeEntity
        {
            Id = 100,
            Status = UserAnimeStatus.None,
            Franchise = new FranchiseContext
            {
                Relation = FranchiseRelationKind.Sequel,
                UserStatus = UserAnimeStatus.Completed,
                RelatedTitle = "Season 1"
            }
        };

        AnimeEntityPresentation.SetDefaultGetLoc((k, args) => args != null && args.Length > 0 ? $"{k}: {args[0]}" : k);

        Assert.True(anime.Presentation.HasFranchiseBadge);
        Assert.False(string.IsNullOrEmpty(anime.Presentation.FranchiseBadgeText));
        Assert.Contains("Season 1", anime.Presentation.FranchiseTooltip);
    }

    [Fact]
    public void HasFranchiseBadge_True_WhenAnimePlanToWatch_AndHasFranchiseRelation()
    {
        var anime = new AnimeEntity
        {
            Id = 100,
            Status = UserAnimeStatus.PlanToWatch,
            Franchise = new FranchiseContext
            {
                Relation = FranchiseRelationKind.Sequel,
                UserStatus = UserAnimeStatus.Completed,
                RelatedTitle = "Season 1"
            }
        };

        Assert.True(anime.Presentation.HasFranchiseBadge);
    }

    [Fact]
    public void FranchiseBadge_DroppedWarning_WhenUserDroppedPreviousPart()
    {
        var anime = new AnimeEntity
        {
            Id = 101,
            Status = UserAnimeStatus.None,
            Franchise = new FranchiseContext
            {
                Relation = FranchiseRelationKind.Sequel,
                UserStatus = UserAnimeStatus.Dropped,
                RelatedTitle = "Old Show"
            }
        };

        Assert.True(anime.Presentation.HasFranchiseBadge);
        Assert.Contains("Old Show", anime.Presentation.FranchiseTooltip);
    }

    [Fact]
    public async Task FranchiseService_BuildsIndex_DirectAndInvertedRelations()
    {
        var userLibrary = new List<AnimeEntity>
        {
            new AnimeEntity { Id = 10, Title = "Show S1", Status = UserAnimeStatus.Completed },
            new AnimeEntity { Id = 20, Title = "Dropped Anime", Status = UserAnimeStatus.Dropped }
        };

        var relations = new List<AnimeRelation>
        {
            // Direct: 10 has Sequel 11 (Show S2)
            new AnimeRelation { SourceMalId = 10, TargetMalId = 11, RelationType = "Sequel" },
            // Inverted: 21 has Prequel 20 (Dropped Anime) => 21 is a Sequel to 20
            new AnimeRelation { SourceMalId = 21, TargetMalId = 20, RelationType = "Prequel" }
        };

        var mockAnimeRepo = new Mock<IAnimeRepository>();
        mockAnimeRepo.Setup(r => r.GetSnapshotAsync()).ReturnsAsync(userLibrary);
        mockAnimeRepo.Setup(r => r.InitializationTask).Returns(Task.CompletedTask);

        var mockRelationRepo = new Mock<IAnimeRelationRepository>();
        mockRelationRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(relations);

        var mockShikiApi = new Mock<Kiriha.Core.Abstractions.Services.IShikiApiService>();

        using var franchiseService = new FranchiseService(mockAnimeRepo.Object, mockRelationRepo.Object, mockShikiApi.Object);
        await franchiseService.RebuildIndexAsync();

        // 11 should be detected as Sequel to Show S1 (Completed)
        var ctx11 = franchiseService.GetFranchiseContext(11);
        Assert.NotNull(ctx11);
        Assert.Equal(FranchiseRelationKind.Sequel, ctx11.Relation);
        Assert.Equal(UserAnimeStatus.Completed, ctx11.UserStatus);
        Assert.Equal("Show S1", ctx11.RelatedTitle);

        // 21 should be detected as Sequel to Dropped Anime (Dropped)
        var ctx21 = franchiseService.GetFranchiseContext(21);
        Assert.NotNull(ctx21);
        Assert.Equal(FranchiseRelationKind.Sequel, ctx21.Relation);
        Assert.Equal(UserAnimeStatus.Dropped, ctx21.UserStatus);
        Assert.Equal("Dropped Anime", ctx21.RelatedTitle);
    }
}
