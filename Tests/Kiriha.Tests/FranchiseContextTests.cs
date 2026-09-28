using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Franchise;
using Moq;

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

    [Fact]
    public async Task FranchiseService_PropagatesMultiHop_DroppedWarning_TanyaCase()
    {
        // Tanya case:
        // User dropped Tanya Season 1 (32615)
        // Movie (37055) is NOT in user list
        // Season 2 (49144) is in Seasonal (not in user list)
        var userLibrary = new List<AnimeEntity>
        {
            new AnimeEntity { Id = 32615, Title = "Youjo Senki", RussianTitle = "Военная хроника маленькой девочки", Status = UserAnimeStatus.Dropped }
        };

        var relations = new List<AnimeRelation>
        {
            new AnimeRelation { SourceMalId = 32615, TargetMalId = 37055, RelationType = "Sequel" },
            new AnimeRelation { SourceMalId = 37055, TargetMalId = 49144, RelationType = "Sequel" }
        };

        var mockAnimeRepo = new Mock<IAnimeRepository>();
        mockAnimeRepo.Setup(r => r.GetSnapshotAsync()).ReturnsAsync(userLibrary);
        mockAnimeRepo.Setup(r => r.InitializationTask).Returns(Task.CompletedTask);

        var mockRelationRepo = new Mock<IAnimeRelationRepository>();
        mockRelationRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(relations);

        var mockShikiApi = new Mock<Kiriha.Core.Abstractions.Services.IShikiApiService>();

        using var franchiseService = new FranchiseService(mockAnimeRepo.Object, mockRelationRepo.Object, mockShikiApi.Object);
        await franchiseService.RebuildIndexAsync();

        // 49144 (Tanya 2) is 2 hops away from 32615 (Tanya 1) and must be detected as Dropped warning!
        var ctxTanya2 = franchiseService.GetFranchiseContext(49144);
        Assert.NotNull(ctxTanya2);
        Assert.False(ctxTanya2.IsMixed);
        Assert.Equal(UserAnimeStatus.Dropped, ctxTanya2.UserStatus);
        Assert.Equal(FranchiseRelationKind.Sequel, ctxTanya2.Relation);
        Assert.Equal("Военная хроника маленькой девочки", ctxTanya2.RelatedTitle);
    }

    [Fact]
    public async Task FranchiseService_DetectsMixedStatus_WhenDroppedAndCompletedExist()
    {
        // Mixed franchise:
        // User completed Season 1 (10)
        // User dropped Movie (20)
        // Season 2 (30) is coming out in Seasonal
        var userLibrary = new List<AnimeEntity>
        {
            new AnimeEntity { Id = 10, Title = "Season 1", Status = UserAnimeStatus.Completed },
            new AnimeEntity { Id = 20, Title = "Movie", Status = UserAnimeStatus.Dropped }
        };

        var relations = new List<AnimeRelation>
        {
            new AnimeRelation { SourceMalId = 10, TargetMalId = 20, RelationType = "Sequel" },
            new AnimeRelation { SourceMalId = 20, TargetMalId = 30, RelationType = "Sequel" }
        };

        var mockAnimeRepo = new Mock<IAnimeRepository>();
        mockAnimeRepo.Setup(r => r.GetSnapshotAsync()).ReturnsAsync(userLibrary);
        mockAnimeRepo.Setup(r => r.InitializationTask).Returns(Task.CompletedTask);

        var mockRelationRepo = new Mock<IAnimeRelationRepository>();
        mockRelationRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(relations);

        var mockShikiApi = new Mock<Kiriha.Core.Abstractions.Services.IShikiApiService>();

        using var franchiseService = new FranchiseService(mockAnimeRepo.Object, mockRelationRepo.Object, mockShikiApi.Object);
        await franchiseService.RebuildIndexAsync();

        var ctxS2 = franchiseService.GetFranchiseContext(30);
        Assert.NotNull(ctxS2);
        Assert.True(ctxS2.IsMixed);
        Assert.Equal("Season 1", ctxS2.CompletedTitle);
        Assert.Equal("Movie", ctxS2.DroppedTitle);
    }

    [Fact]
    public void Presentation_MixedStatus_BadgeAndTooltip()
    {
        var anime = new AnimeEntity
        {
            Id = 30,
            Status = UserAnimeStatus.None,
            Franchise = new FranchiseContext
            {
                Relation = FranchiseRelationKind.Sequel,
                UserStatus = UserAnimeStatus.Completed,
                IsMixed = true,
                CompletedTitle = "Season 1",
                DroppedTitle = "Movie"
            }
        };

        AnimeEntityPresentation.SetDefaultGetLoc((k, args) => args != null && args.Length > 0 ? $"{k}: {string.Join(", ", args)}" : k);

        Assert.True(anime.Presentation.HasFranchiseBadge);
        Assert.Equal("anime.labels.franchise_mixed", anime.Presentation.FranchiseBadgeText);
        Assert.Contains("Season 1", anime.Presentation.FranchiseTooltip);
        Assert.Contains("Movie", anime.Presentation.FranchiseTooltip);
    }

    [Fact]
    public void FranchiseConverters_HandleMixedStatus()
    {
        var ctx = new FranchiseContext
        {
            Relation = FranchiseRelationKind.Sequel,
            UserStatus = UserAnimeStatus.Completed,
            IsMixed = true,
            CompletedTitle = "Season 1",
            DroppedTitle = "Movie"
        };

        var colorConverter = new Kiriha.Views.Converters.FranchiseToColorConverter();
        var brush = colorConverter.Convert(ctx, typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture) as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(brush);
        Assert.Equal(Avalonia.Media.Color.Parse("#D97706"), brush.Color);

        var iconConverter = new Kiriha.Views.Converters.FranchiseToIconConverter();
        var icon = iconConverter.Convert(ctx, typeof(Material.Icons.MaterialIconKind), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Material.Icons.MaterialIconKind.AlertCircleOutline, icon);
    }
}
