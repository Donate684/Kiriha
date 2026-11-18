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

    [Fact]
    public void SecondaryTitle_DefaultsToEnglishAndAvoidsDuplication()
    {
        AnimeEntityPresentation.GetUseRussianTitles = () => false;

        var item = new AnimeEntity
        {
            Title = "Kimi no Na wa.",
            EnglishTitle = "Your Name.",
            RussianTitle = "Твоё имя"
        };

        Assert.Equal("Your Name.", item.SecondaryTitle);
        Assert.True(item.HasSecondaryTitle);

        var duplicateItem = new AnimeEntity
        {
            Title = "Naruto",
            EnglishTitle = "Naruto"
        };

        Assert.Null(duplicateItem.SecondaryTitle);
        Assert.False(duplicateItem.HasSecondaryTitle);
    }

    [Fact]
    public void SecondaryTitle_SwitchesToRussianWhenRussianTitlesEnabled()
    {
        var item = new AnimeEntity
        {
            Title = "Sousou no Frieren",
            EnglishTitle = "Frieren: Beyond Journey's End",
            RussianTitle = "Провожающая в последний путь Фрирен"
        };

        // When Russian Titles enabled -> Russian replaces English
        AnimeEntityPresentation.GetUseRussianTitles = () => true;
        Assert.Equal("Провожающая в последний путь Фрирен", item.SecondaryTitle);

        // When Russian Titles disabled -> English is used
        AnimeEntityPresentation.GetUseRussianTitles = () => false;
        Assert.Equal("Frieren: Beyond Journey's End", item.SecondaryTitle);

        // When Russian Titles enabled but RussianTitle is empty -> falls back to English
        var itemWithoutRussian = new AnimeEntity
        {
            Title = "Sousou no Frieren",
            EnglishTitle = "Frieren: Beyond Journey's End"
        };
        AnimeEntityPresentation.GetUseRussianTitles = () => true;
        Assert.Equal("Frieren: Beyond Journey's End", itemWithoutRussian.SecondaryTitle);

        // Reset delegate
        AnimeEntityPresentation.GetUseRussianTitles = () => false;
    }

    [Fact]
    public void Presentation_IsLazyAndNotAllocatedInDefaultConstructor()
    {
        var item = new AnimeEntity { Title = "Lazy Test" };
        Assert.False(item.IsPresentationCreated);

        var presentation = item.Presentation;
        Assert.NotNull(presentation);
        Assert.True(item.IsPresentationCreated);
        Assert.Same(presentation, item.Presentation);
    }

    [Fact]
    public void Clone_ResetsPresentationInstance()
    {
        var item = new AnimeEntity { Id = 1, Title = "Original", Progress = 5 };
        var originalPresentation = item.Presentation;
        Assert.NotNull(originalPresentation);

        var clone = item.Clone();
        clone.Progress = 10;

        Assert.NotSame(originalPresentation, clone.Presentation);
        Assert.Equal(5, originalPresentation.EffectiveProgress);
        Assert.Equal(10, clone.Presentation.EffectiveProgress);
    }

    [Fact]
    public void JapaneseTitle_PrefersNativeJapanese_WhenRussianTitleIsDisplayTitle()
    {
        var item = new AnimeEntity
        {
            Title = "Baby Princess 3D Paradise 0 [Love]",
            RussianTitle = "Юные принцессы: Райское влечение",
            JapaneseTitle = "Baby Princess 3D ぱらだいす0［ラブ］"
        };

        Assert.Equal("Юные принцессы: Райское влечение", item.Presentation.DisplayTitle);
        Assert.Equal("Baby Princess 3D ぱらだいす0［ラブ］", item.Presentation.JapaneseTitle);
        Assert.True(item.Presentation.HasJapaneseTitle);
        Assert.Equal("Baby Princess 3D ぱらだいす0［ラブ］", item.JapaneseDisplayTitle);
        Assert.True(item.HasJapaneseTitle);
    }

    [Fact]
    public void JapaneseTitle_FallsBackToRomajiTitle_WhenNativeJapaneseIsMissing()
    {
        var item = new AnimeEntity
        {
            Title = "Baby Princess 3D Paradise 0 [Love]",
            RussianTitle = "Юные принцессы: Райское влечение",
            JapaneseTitle = null
        };

        Assert.Equal("Юные принцессы: Райское влечение", item.Presentation.DisplayTitle);
        Assert.Equal("Baby Princess 3D Paradise 0 [Love]", item.Presentation.JapaneseTitle);
        Assert.True(item.Presentation.HasJapaneseTitle);
    }

    [Fact]
    public void JapaneseTitle_ReturnsNull_WhenJapaneseMatchesDisplayTitle()
    {
        var item = new AnimeEntity
        {
            Title = "Sousou no Frieren",
            RussianTitle = null,
            JapaneseTitle = "Sousou no Frieren"
        };

        Assert.Equal("Sousou no Frieren", item.Presentation.DisplayTitle);
        Assert.Null(item.Presentation.JapaneseTitle);
        Assert.False(item.Presentation.HasJapaneseTitle);
    }

    [Fact]
    public void JapaneseTitle_ReturnsNativeJapanese_WhenRomajiIsDisplayTitle()
    {
        var item = new AnimeEntity
        {
            Title = "Sousou no Frieren",
            RussianTitle = null,
            JapaneseTitle = "葬送のフリーレン"
        };

        Assert.Equal("Sousou no Frieren", item.Presentation.DisplayTitle);
        Assert.Equal("葬送のフリーレン", item.Presentation.JapaneseTitle);
        Assert.True(item.Presentation.HasJapaneseTitle);
    }

    [Fact]
    public void TitleAndSecondaryTitle_MatchCardAndHeaderSymmetry()
    {
        var item = new AnimeEntity
        {
            Title = "Baby Princess 3D Paradise 0 [Love]",
            RussianTitle = "Юные принцессы: Райское влечение",
            JapaneseTitle = "Baby Princess 3D ぱらだいす0［ラブ］"
        };

        AnimeEntityPresentation.GetUseRussianTitles = () => true;
        try
        {
            // Top line across cards, details hero, and now playing header
            Assert.Equal("Baby Princess 3D Paradise 0 [Love]", item.RomajiTitle);

            // Bottom line across cards, details hero, and now playing header
            Assert.Equal("Юные принцессы: Райское влечение", item.Presentation.SecondaryTitle);
            Assert.True(item.Presentation.HasSecondaryTitle);
        }
        finally
        {
            AnimeEntityPresentation.GetUseRussianTitles = () => false;
        }
    }

    [Fact]
    public void RomajiTitle_HealsFromAlternativeTitles_WhenTitleWasOverwrittenWithEnglishTitle()
    {
        var item = new AnimeEntity
        {
            Title = "Reincarnated as a Sword Season 2",
            EnglishTitle = "Reincarnated as a Sword Season 2",
            RussianTitle = "О моём перерождении в меч 2",
            JapaneseTitle = "転生したら剣でした 2",
            AlternativeTitles = ["Tensei shitara Ken Deshita 2", "Reincarnated as a Sword Season 2", "転生したら剣でした 2"]
        };

        // When Title was mistakenly English, RomajiTitle extracts the romaji from AlternativeTitles and heals Title
        Assert.Equal("Tensei shitara Ken Deshita 2", item.RomajiTitle);
        Assert.Equal("Tensei shitara Ken Deshita 2", item.Title);

        // When Russian titles are enabled: bottom title is Russian
        AnimeEntityPresentation.GetUseRussianTitles = () => true;
        try
        {
            Assert.Equal("О моём перерождении в меч 2", item.Presentation.SecondaryTitle);
            Assert.True(item.Presentation.HasSecondaryTitle);
        }
        finally
        {
            AnimeEntityPresentation.GetUseRussianTitles = () => false;
        }

        // When Russian titles are disabled: bottom title falls back to English
        AnimeEntityPresentation.GetUseRussianTitles = () => false;
        Assert.Equal("Reincarnated as a Sword Season 2", item.Presentation.SecondaryTitle);
        Assert.True(item.Presentation.HasSecondaryTitle);
    }

    [Fact]
    public void AniListMapper_PrioritizesRomajiForTitle_AndEnglishForEnglishTitle()
    {
        using var aniDoc = System.Text.Json.JsonDocument.Parse("""
        {
            "id": 154587,
            "idMal": 53913,
            "title": {
                "romaji": "Tensei shitara Ken Deshita 2",
                "english": "Reincarnated as a Sword Season 2",
                "native": "転生したら剣でした 2"
            }
        }
        """);

        var entity = Kiriha.Core.Tracking.Api.AniListMapper.MapMediaToAnimeEntity(aniDoc.RootElement);

        Assert.Equal("Tensei shitara Ken Deshita 2", entity.Title);
        Assert.Equal("Tensei shitara Ken Deshita 2", entity.RomajiTitle);
        Assert.Equal("Reincarnated as a Sword Season 2", entity.EnglishTitle);
        Assert.Equal("転生したら剣でした 2", entity.JapaneseTitle);
    }

    [Fact]
    public void ShikiMapper_ExtractsEnglishTitleFromMediaNode_AndPreservesRomajiTitle()
    {
        using var rateDoc = System.Text.Json.JsonDocument.Parse("""
        {
            "anime": {
                "id": 53913,
                "name": "Tensei shitara Ken deshita 2nd Season",
                "russian": "О моём перерождении в меч 2",
                "english": [ "Reincarnated as a Sword Season 2" ],
                "japanese": [ "転生したら剣でした 第2期" ]
            }
        }
        """);

        var entity = Kiriha.Core.Tracking.Api.ShikiMapper.MapRateToAnimeEntity(rateDoc.RootElement, "https://shikimori.one");

        Assert.Equal("Tensei shitara Ken deshita 2nd Season", entity.Title);
        Assert.Equal("Tensei shitara Ken deshita 2nd Season", entity.RomajiTitle);
        Assert.Equal("Reincarnated as a Sword Season 2", entity.EnglishTitle);
        Assert.Equal("О моём перерождении в меч 2", entity.RussianTitle);
        Assert.Equal("転生したら剣でした 第2期", entity.JapaneseTitle);
    }
}
