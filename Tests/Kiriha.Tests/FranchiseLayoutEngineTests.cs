using System.Collections.Generic;
using System.Linq;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Utils.Graphs;
using Xunit;

namespace Kiriha.Tests;

public class FranchiseLayoutEngineTests
{
    private static ShikiFranchiseResponse CreateSampleKusuriyaFranchise()
    {
        return new ShikiFranchiseResponse
        {
            CurrentId = 1,
            Nodes = new List<ShikiFranchiseNode>
            {
                new() { Id = 1, Name = "Kusuriya no Hitorigoto", Kind = "tv", Year = 2023, Date = 1696000000, Weight = 10 },
                new() { Id = 2, Name = "Maomao no Hitorigoto", Kind = "ona", Year = 2023, Date = 1697000000, Weight = 1 },
                new() { Id = 3, Name = "Kusuriya no Hitorigoto 2nd Season", Kind = "tv", Year = 2025, Date = 1736000000, Weight = 9 },
                new() { Id = 4, Name = "Kusuriya no Hitorigoto Movie: Bouhi no Hihou", Kind = "movie", Year = 2026, Date = 1768000000, Weight = 8 },
                new() { Id = 5, Name = "Kusuriya no Hitorigoto Manga", Kind = "manga", Year = 2017, Date = 1495000000, Weight = 7 }
            },
            Links = new List<ShikiFranchiseLink>
            {
                new() { SourceId = 1, TargetId = 3, Relation = "sequel" },
                new() { SourceId = 1, TargetId = 2, Relation = "side_story" },
                new() { SourceId = 3, TargetId = 4, Relation = "side_story" },
                new() { SourceId = 5, TargetId = 1, Relation = "alternative_version" }
            }
        };
    }

    [Fact]
    public void CalculateLayout_PlacesMainSpineOnGridXZero()
    {
        var data = CreateSampleKusuriyaFranchise();
        var options = new FranchiseLayoutOptions { HideSpecials = false, AnimeOnly = false };

        var layout = FranchiseLayoutEngine.CalculateLayout(data, options);

        var s1 = layout.Nodes.FirstOrDefault(n => n.Node.Id == 1);
        var s2 = layout.Nodes.FirstOrDefault(n => n.Node.Id == 3);

        Assert.NotNull(s1);
        Assert.NotNull(s2);
        Assert.True(s1.IsMainLine);
        Assert.True(s2.IsMainLine);
        Assert.Equal(0, s1.GridX);
        Assert.Equal(0, s2.GridX);
        Assert.True(s2.GridY > s1.GridY);
    }

    [Fact]
    public void CalculateLayout_PlacesMovieOffMainSpine()
    {
        var data = CreateSampleKusuriyaFranchise();
        var options = new FranchiseLayoutOptions { HideSpecials = false, AnimeOnly = false };

        var layout = FranchiseLayoutEngine.CalculateLayout(data, options);

        var movie = layout.Nodes.FirstOrDefault(n => n.Node.Id == 4);

        Assert.NotNull(movie);
        Assert.False(movie.IsMainLine);
        Assert.NotEqual(0, movie.GridX);
    }

    [Fact]
    public void CalculateLayout_FiltersMangaWhenAnimeOnlyIsTrue()
    {
        var data = CreateSampleKusuriyaFranchise();
        var options = new FranchiseLayoutOptions { AnimeOnly = true, HideSpecials = false };

        var layout = FranchiseLayoutEngine.CalculateLayout(data, options);

        Assert.DoesNotContain(layout.Nodes, n => n.Node.Id == 5);
    }

    [Fact]
    public void CalculateLayout_FiltersChibiWhenHideSpecialsIsTrue()
    {
        var data = CreateSampleKusuriyaFranchise();
        var options = new FranchiseLayoutOptions { AnimeOnly = true, HideSpecials = true };

        var layout = FranchiseLayoutEngine.CalculateLayout(data, options);

        // Id 2 is Maomao no Hitorigoto (chibi ONA with weight 1)
        Assert.DoesNotContain(layout.Nodes, n => n.Node.Id == 2);
    }

    [Fact]
    public void CalculateLayout_TimelineNodes_AreSortedByDateWithSteps()
    {
        var data = CreateSampleKusuriyaFranchise();
        var options = new FranchiseLayoutOptions { AnimeOnly = true, HideSpecials = false };

        var layout = FranchiseLayoutEngine.CalculateLayout(data, options);

        Assert.NotEmpty(layout.TimelineNodes);

        for (int i = 0; i < layout.TimelineNodes.Count; i++)
        {
            Assert.Equal(i + 1, layout.TimelineNodes[i].StepIndex);
        }

        // Verify strictly non-decreasing dates
        for (int i = 0; i < layout.TimelineNodes.Count - 1; i++)
        {
            Assert.True(layout.TimelineNodes[i].Node.Date <= layout.TimelineNodes[i + 1].Node.Date);
        }
    }

    [Fact]
    public void CalculateLayout_GeneratesArrowPathsAndLinks()
    {
        var data = CreateSampleKusuriyaFranchise();
        var options = new FranchiseLayoutOptions { AnimeOnly = true, HideSpecials = false };

        var layout = FranchiseLayoutEngine.CalculateLayout(data, options);

        Assert.NotEmpty(layout.Links);
        var mainLink = layout.Links.FirstOrDefault(l => l.Source.Node.Id == 1 && l.Target.Node.Id == 3);

        Assert.NotNull(mainLink);
        Assert.True(mainLink.IsMainLine);
        Assert.False(string.IsNullOrEmpty(mainLink.ConnectionPath));
        Assert.False(string.IsNullOrEmpty(mainLink.ArrowPath));
    }
}
