using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Core;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.Genres;
using Xunit;

namespace Kiriha.Tests;

public sealed class GenreSearchTests
{
    private static List<AnimeEntity> CreateTestCollection() =>
    [
        new AnimeEntity
        {
            Id = 1,
            Title = "Sword Art Online: Extra Edition",
            RussianTitle = "Мастера Меча Онлайн: Экстра",
            Status = UserAnimeStatus.Watching,
            Genres = ["Action", "Adventure", "Fantasy", "Ecchi"]
        },
        new AnimeEntity
        {
            Id = 2,
            Title = "Clannad",
            RussianTitle = "Кланнад",
            Status = UserAnimeStatus.Watching,
            Genres = ["Drama", "Romance", "Supernatural"]
        },
        new AnimeEntity
        {
            Id = 3,
            Title = "Highschool DxD",
            RussianTitle = "Демоны старшей школы",
            Status = UserAnimeStatus.Watching,
            Genres = ["Comedy", "Ecchi", "Harem", "Romance"]
        },
        new AnimeEntity
        {
            Id = 4,
            Title = "Steins;Gate",
            RussianTitle = "Врата Штейна",
            Status = UserAnimeStatus.Watching,
            Genres = ["Drama", "Sci-Fi", "Suspense"]
        }
    ];

    [Theory]
    [InlineData("Ecchi", new[] { 1, 3 })]
    [InlineData("ecchi", new[] { 1, 3 })]
    [InlineData("Эччи", new[] { 1, 3 })]
    [InlineData("эччи", new[] { 1, 3 })]
    [InlineData("этти", new[] { 1, 3 })]
    [InlineData("#ecchi", new[] { 1, 3 })]
    [InlineData("#эччи", new[] { 1, 3 })]
    [InlineData("tag:ecchi", new[] { 1, 3 })]
    public void ApplySearch_FindsAnimeByEcchiTagInRussianAndEnglish(string query, int[] expectedIds)
    {
        var items = CreateTestCollection();
        var results = items.ApplySearch(query).Select(x => x.Id).ToArray();
        Assert.Equal(expectedIds, results);
    }

    [Theory]
    [InlineData("Drama", new[] { 2, 4 })]
    [InlineData("Драма", new[] { 2, 4 })]
    [InlineData("#драма", new[] { 2, 4 })]
    [InlineData("tag:drama", new[] { 2, 4 })]
    public void ApplySearch_FindsAnimeByDramaTagInRussianAndEnglish(string query, int[] expectedIds)
    {
        var items = CreateTestCollection();
        var results = items.ApplySearch(query).Select(x => x.Id).ToArray();
        Assert.Equal(expectedIds, results);
    }

    [Fact]
    public void ApplySearch_CombinesGenreAndTitleSearch()
    {
        var items = CreateTestCollection();

        // "эччи sword" -> genre Ecchi + title "sword" -> only SAO (Id 1), not Highschool DxD (Id 3)
        var resultsRu = items.ApplySearch("эччи sword").Select(x => x.Id).ToArray();
        Assert.Equal(new[] { 1 }, resultsRu);

        // "драма кланнад" -> genre Drama + title "кланнад" -> only Clannad (Id 2)
        var resultsDrama = items.ApplySearch("драма кланнад").Select(x => x.Id).ToArray();
        Assert.Equal(new[] { 2 }, resultsDrama);

        // "эччи демоны" -> only Highschool DxD (Id 3)
        var resultsDxD = items.ApplySearch("эччи демоны").Select(x => x.Id).ToArray();
        Assert.Equal(new[] { 3 }, resultsDxD);
    }

    [Fact]
    public void AnimeCollectionProjection_Query_FiltersByGenreTags()
    {
        var items = CreateTestCollection();
        var projection = new AnimeCollectionProjection();
        projection.Rebuild(items);

        // Query by active genre key
        var ecchiResults = projection.Query(
            UserAnimeStatus.Watching,
            searchQuery: string.Empty,
            activeGenreKeys: ["ecchi"],
            filterNsfw: false,
            sortBy: "Title",
            kind: MediaKind.Anime);

        Assert.Equal(new[] { 1, 3 }, ecchiResults.Select(x => x.Id).Order().ToArray());

        // Query with search string "Драма"
        var dramaResults = projection.Query(
            UserAnimeStatus.Watching,
            searchQuery: "Драма",
            activeGenreKeys: null,
            filterNsfw: false,
            sortBy: "Title",
            kind: MediaKind.Anime);

        Assert.Equal(new[] { 2, 4 }, dramaResults.Select(x => x.Id).Order().ToArray());

        // Query combining active chip + title search
        var chipAndTitle = projection.Query(
            UserAnimeStatus.Watching,
            searchQuery: "мастера",
            activeGenreKeys: ["ecchi"],
            filterNsfw: false,
            sortBy: "Title",
            kind: MediaKind.Anime);

        Assert.Equal(new[] { 1 }, chipAndTitle.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void GenreCatalog_SearchGenres_ReturnsRelevantSuggestions()
    {
        var ecchiSuggestions = GenreCatalog.SearchGenres("эч", 5);
        Assert.Contains(ecchiSuggestions, g => g.Key == "ecchi");
        Assert.Equal("ecchi", ecchiSuggestions[0].Key);

        var dramaSuggestions = GenreCatalog.SearchGenres("дра", 5);
        Assert.Contains(dramaSuggestions, g => g.Key == "drama");
        Assert.Equal("drama", dramaSuggestions[0].Key);
    }
}
