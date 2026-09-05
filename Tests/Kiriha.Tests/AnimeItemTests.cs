using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Tests;

public sealed class AnimeEntityTests
{
    [Fact]
    public void DisplayTitle_AndSynopsisPreferRussianWhenPresent()
    {
        var item = new AnimeEntity
        {
            Title = "Frieren: Beyond Journey's End",
            RussianTitle = "??????????? ? ????????? ???? ??????",
            Synopsis = "English synopsis",
            RussianSynopsis = "Russian synopsis"
        };

        Assert.Equal("??????????? ? ????????? ???? ??????", item.Presentation.DisplayTitle);
        Assert.Equal("Russian synopsis", item.Presentation.DisplaySynopsis);
    }

    [Theory]
    [InlineData(6, 12, 50)]
    [InlineData(20, 24, 83.33333333333334)]
    [InlineData(30, 0, 83.33333333333334)]
    [InlineData(0, 0, 0)]
    public void ProgressValue_UsesKnownTotalOrBucketedFallback(int progress, int total, double expected)
    {
        var item = new AnimeEntity { Progress = progress, TotalEpisodes = total };

        Assert.Equal(expected, item.Presentation.ProgressValue, precision: 6);
    }

    [Fact]
    public void AiredProgress_ShowsOnlyWhenWatchingHasUnseenAiredEpisodes()
    {
        var item = new AnimeEntity
        {
            Status = UserAnimeStatus.Watching,
            Progress = 3,
            TotalEpisodes = 12,
            EpisodesAired = 5,
            StatusDetailed = "currently_airing"
        };

        Assert.True(item.Presentation.ShowAiredProgressBar);
        Assert.Equal(2, item.Presentation.UnseenEpisodesCount);
        Assert.Equal(5d / 12d, item.Presentation.AiredValueFraction, precision: 6);
    }

    [Fact]
    public void Presentation_MatchesAnimeEntityCompatibilityProperties()
    {
        var item = new AnimeEntity
        {
            Title = "Original",
            RussianTitle = "Localized",
            Synopsis = "Synopsis",
            RussianSynopsis = "Localized synopsis",
            Status = UserAnimeStatus.Watching,
            Progress = 3,
            TotalEpisodes = 12,
            EpisodesAired = 5,
            StatusDetailed = "currently_airing",
            Genres = new List<string> { "Action", "Drama", "Comedy" },
            Studios = new List<string> { "Madhouse" }
        };

        var presentation = item.Presentation;

        Assert.Equal(item.Presentation.DisplayTitle, presentation.DisplayTitle);
        Assert.Equal(item.Presentation.DisplaySynopsis, presentation.DisplaySynopsis);
        Assert.Equal(item.Presentation.ProgressValue, presentation.ProgressValue);
        Assert.Equal(item.Presentation.AiredValueFraction, presentation.AiredValueFraction);
        Assert.Equal(item.Presentation.ShowAiredProgressBar, presentation.ShowAiredProgressBar);
        Assert.Equal(item.Presentation.UnseenEpisodesCount, presentation.UnseenEpisodesCount);
        Assert.Equal(item.Presentation.TopGenres, presentation.TopGenres);
        Assert.Equal(item.Presentation.HasStudios, presentation.HasStudios);
    }

    [Fact]
    public void Presentation_UsesSnapshotTimeForTimeDependentBadges()
    {
        var now = new DateTime(2026, 06, 01, 12, 00, 00);
        var item = new AnimeEntity
        {
            NextEpisodeAt = now.AddHours(-49)
        };

        var presentation = new AnimeEntityPresentation(item, now);

        Assert.Equal(string.Empty, presentation.AiringBadgeText);
        Assert.Equal("#FF8C00", presentation.AiringBadgeColor);
    }

    [Fact]
    public void Presentation_NotifiesWithoutLegacyComputedPropertyNoise()
    {
        var item = new AnimeEntity();
        var changed = new List<string?>();
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        item.Progress = 3;

        Assert.Contains(nameof(AnimeEntity.Presentation), changed);
        Assert.DoesNotContain(nameof(AnimeEntityPresentation.ProgressValue), changed);
        Assert.DoesNotContain(nameof(AnimeEntityPresentation.ProgressValueFraction), changed);
    }

    [Fact]
    public void Clone_CopiesCollectionsWithoutSharingListInstances()
    {
        var item = new AnimeEntity
        {
            Id = 1,
            Title = "Test",
            Genres = new List<string> { "Action" },
            Studios = new List<string> { "Bones" },
            AlternativeTitles = new List<string> { "Alt" }
        };

        var clone = item.Clone();
        clone.Genres.Add("Drama");
        clone.Studios.Add("Trigger");
        clone.AlternativeTitles.Add("Other");

        Assert.Equal(new[] { "Action" }, item.Genres);
        Assert.Equal(new[] { "Bones" }, item.Studios);
        Assert.Equal(new[] { "Alt" }, item.AlternativeTitles);
    }

    [Fact]
    public void Genres_NotifiesPropertyChanged()
    {
        var item = new AnimeEntity();
        var changed = new List<string?>();
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        item.Genres = new List<string> { "Action", "Fantasy" };

        Assert.Contains(nameof(AnimeEntity.Genres), changed);
        Assert.Contains(nameof(AnimeEntity.Presentation), changed);
        Assert.True(item.Presentation.HasGenres);
    }
}
