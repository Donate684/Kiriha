using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.Formats;
using Kiriha.Core.Domain.Models.Genres;
using Xunit;

namespace Kiriha.Tests;

public sealed class FormatSearchTests
{
    private static List<AnimeEntity> CreateTestCollection() =>
    [
        new AnimeEntity
        {
            Id = 1,
            Title = "A Silent Voice",
            RussianTitle = "Форма голоса",
            Type = AppConstants.AnimeTypes.Movie,
            Status = UserAnimeStatus.Watching,
            Genres = ["Drama", "Romance"]
        },
        new AnimeEntity
        {
            Id = 2,
            Title = "Clannad",
            RussianTitle = "Кланнад",
            Type = AppConstants.AnimeTypes.Tv,
            Status = UserAnimeStatus.Watching,
            Genres = ["Drama", "Romance"]
        },
        new AnimeEntity
        {
            Id = 3,
            Title = "Rurouni Kenshin: Trust and Betrayal",
            RussianTitle = "Бродяга Кэнсин: Воспоминания",
            Type = AppConstants.AnimeTypes.Ova,
            Status = UserAnimeStatus.Watching,
            Genres = ["Action", "Drama"]
        },
        new AnimeEntity
        {
            Id = 4,
            Title = "Cyberpunk: Edgerunners",
            RussianTitle = "Киберпанк: Бегущие по краю",
            Type = AppConstants.AnimeTypes.Ona,
            Status = UserAnimeStatus.Watching,
            Genres = ["Action", "Sci-Fi"]
        },
        new AnimeEntity
        {
            Id = 5,
            Title = "Gintama: Jump Festa",
            RussianTitle = "Гинтама: Праздник Джамп",
            Type = AppConstants.AnimeTypes.Special,
            Status = UserAnimeStatus.Watching,
            Genres = ["Comedy"]
        },
        new AnimeEntity
        {
            Id = 6,
            Title = "One Piece: Episode of East Blue",
            RussianTitle = "Ван-Пис: Эпизод Ист-Блю",
            Type = AppConstants.AnimeTypes.TvSpecial,
            Status = UserAnimeStatus.Watching,
            Genres = ["Action", "Adventure"]
        }
    ];

    [Theory]
    [InlineData("movie", "movie")]
    [InlineData("Movie", "movie")]
    [InlineData("фильм", "movie")]
    [InlineData("Фильм", "movie")]
    [InlineData("мувик", "movie")]
    [InlineData("полнометражка", "movie")]
    [InlineData("ova", "ova")]
    [InlineData("OVA", "ova")]
    [InlineData("ова", "ova")]
    [InlineData("овашка", "ova")]
    [InlineData("ona", "ona")]
    [InlineData("ONA", "ona")]
    [InlineData("она", "ona")]
    [InlineData("special", "special")]
    [InlineData("спешл", "special")]
    [InlineData("спецвыпуск", "special")]
    [InlineData("tv", "tv")]
    [InlineData("тв", "tv")]
    [InlineData("сериал", "tv")]
    public void FormatCatalog_TryFindFormat_RecognizesFormatsAndAliases(string query, string expectedKey)
    {
        bool found = FormatCatalog.TryFindFormat(query, out var format);
        Assert.True(found);
        Assert.NotNull(format);
        Assert.Equal(expectedKey, format!.Key);
    }

    [Theory]
    [InlineData("фильм", new[] { 1 })]
    [InlineData("movie", new[] { 1 })]
    [InlineData("мувик", new[] { 1 })]
    [InlineData("ova", new[] { 3 })]
    [InlineData("ова", new[] { 3 })]
    [InlineData("ona", new[] { 4 })]
    [InlineData("она", new[] { 4 })]
    [InlineData("спешл", new[] { 5, 6 })]
    [InlineData("special", new[] { 5, 6 })]
    [InlineData("сериал", new[] { 2 })]
    [InlineData("тв", new[] { 2 })]
    public void ApplySearch_FiltersByFormatTokens(string query, int[] expectedIds)
    {
        var items = CreateTestCollection();
        var results = items.ApplySearch(query).Select(x => x.Id).ToArray();
        Assert.Equal(expectedIds, results);
    }

    [Fact]
    public void ApplySearch_CombinesFormatAndGenreAndTitle()
    {
        var items = CreateTestCollection();

        // "фильм драма" -> format Movie (Id 1) + genre Drama -> Id 1 (Clannad is TV, so excluded)
        var movieDrama = items.ApplySearch("фильм драма").Select(x => x.Id).ToArray();
        Assert.Equal(new[] { 1 }, movieDrama);

        // "тв драма" -> format TV (Id 2) + genre Drama -> Id 2
        var tvDrama = items.ApplySearch("тв драма").Select(x => x.Id).ToArray();
        Assert.Equal(new[] { 2 }, tvDrama);

        // "ova кэнсин" -> format OVA (Id 3) + title "кэнсин" -> Id 3
        var ovaTitle = items.ApplySearch("ova кэнсин").Select(x => x.Id).ToArray();
        Assert.Equal(new[] { 3 }, ovaTitle);

        // "ona киберпанк" -> format ONA (Id 4) + title "киберпанк" -> Id 4
        var onaTitle = items.ApplySearch("ona киберпанк").Select(x => x.Id).ToArray();
        Assert.Equal(new[] { 4 }, onaTitle);
    }

    [Fact]
    public void AnimeCollectionProjection_Query_FiltersByFormatKeys()
    {
        var items = CreateTestCollection();
        var projection = new AnimeCollectionProjection();
        projection.Rebuild(items);

        // Query by active format key "movie"
        var movieResults = projection.Query(
            UserAnimeStatus.Watching,
            searchQuery: string.Empty,
            activeGenreKeys: null,
            activeFormatKeys: ["movie"],
            filterNsfw: false,
            sortBy: "Title",
            kind: MediaKind.Anime);

        Assert.Equal(new[] { 1 }, movieResults.Select(x => x.Id).ToArray());

        // Query by active format keys "movie" OR "ova"
        var movieOrOva = projection.Query(
            UserAnimeStatus.Watching,
            searchQuery: string.Empty,
            activeGenreKeys: null,
            activeFormatKeys: ["movie", "ova"],
            filterNsfw: false,
            sortBy: "Title",
            kind: MediaKind.Anime);

        Assert.Equal(new[] { 1, 3 }, movieOrOva.Select(x => x.Id).Order().ToArray());

        // Query combining active format "tv" and genre "drama"
        var tvDrama = projection.Query(
            UserAnimeStatus.Watching,
            searchQuery: string.Empty,
            activeGenreKeys: ["drama"],
            activeFormatKeys: ["tv"],
            filterNsfw: false,
            sortBy: "Title",
            kind: MediaKind.Anime);

        Assert.Equal(new[] { 2 }, tvDrama.Select(x => x.Id).ToArray());

        // Query combining active format "special" (matches both special and tv_special)
        var specials = projection.Query(
            UserAnimeStatus.Watching,
            searchQuery: string.Empty,
            activeGenreKeys: null,
            activeFormatKeys: ["special"],
            filterNsfw: false,
            sortBy: "Title",
            kind: MediaKind.Anime);

        Assert.Equal(new[] { 5, 6 }, specials.Select(x => x.Id).Order().ToArray());
    }

    [Fact]
    public void AnimeCollectionProjection_CountFormat_ReturnsCorrectCounts()
    {
        var items = CreateTestCollection();
        var projection = new AnimeCollectionProjection();
        projection.Rebuild(items);

        Assert.Equal(1, projection.CountFormat(UserAnimeStatus.Watching, MediaKind.Anime, "movie"));
        Assert.Equal(1, projection.CountFormat(UserAnimeStatus.Watching, MediaKind.Anime, "tv"));
        Assert.Equal(1, projection.CountFormat(UserAnimeStatus.Watching, MediaKind.Anime, "ova"));
        Assert.Equal(1, projection.CountFormat(UserAnimeStatus.Watching, MediaKind.Anime, "ona"));
        Assert.Equal(2, projection.CountFormat(UserAnimeStatus.Watching, MediaKind.Anime, "special"));
    }
}
