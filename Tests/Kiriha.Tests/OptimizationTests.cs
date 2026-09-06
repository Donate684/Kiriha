using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Mapping;
using Xunit;

namespace Kiriha.Tests;

public class OptimizationTests
{
    [Fact]
    public void AnimeEntity_IsNsfw_CachesResult_AndInvalidatesOnPropertyChanges()
    {
        var item = new AnimeEntity
        {
            Id = 1,
            Title = "Test Anime",
            Rating = "pg_13",
            Nsfw = "white"
        };

        // Initially safe
        Assert.False(item.IsNsfw);

        // Update Rating -> should become NSFW and cache should be invalidated
        item.Rating = "rx";
        Assert.True(item.IsNsfw);

        // Reset Rating, set Nsfw -> "black"
        item.Rating = "pg_13";
        Assert.False(item.IsNsfw);

        item.Nsfw = "black";
        Assert.True(item.IsNsfw);

        // Reset Nsfw, add "Hentai" to Genres
        item.Nsfw = "white";
        Assert.False(item.IsNsfw);

        item.Genres = new List<string> { "Action", "Hentai" };
        Assert.True(item.IsNsfw);

        item.Genres = new List<string> { "Action", "Comedy" };
        Assert.False(item.IsNsfw);
    }

    [Fact]
    public void ImageDownloader_GetHashString_MatchesStandardSha256()
    {
        var testUrls = new[]
        {
            "https://cdn.myanimelist.net/images/anime/1000/110533.jpg",
            "https://cdn.myanimelist.net/images/anime/4/19644.jpg?s=c82d4021b473727914041b183dd5d606",
            "http://example.com/a-very-long-url-path/" + new string('x', 600) + ".png"
        };

        foreach (var url in testUrls)
        {
            string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
            string actual = ImageDownloader.GetHashString(url);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ImageDownloader_GetFileNameForUrl_StripsQueryAndPreservesExtension()
    {
        string urlWithQuery = "https://cdn.myanimelist.net/images/anime/4/19644.png?s=c82d4021b4737279";
        string fileName = ImageDownloader.GetFileNameForUrl(urlWithQuery);

        Assert.EndsWith(".png", fileName);
        Assert.DoesNotContain("?", fileName);

        string expectedHash = ImageDownloader.GetHashString(urlWithQuery);
        Assert.Equal(expectedHash + ".png", fileName);

        string urlWithoutExt = "https://cdn.myanimelist.net/images/anime/4/19644?s=123";
        string fileNameDefaultExt = ImageDownloader.GetFileNameForUrl(urlWithoutExt);
        Assert.EndsWith(".jpg", fileNameDefaultExt);
    }

    [Fact]
    public async Task RecognitionCache_ConcurrentAccess_IsThreadSafe()
    {
        var cache = new RecognitionCache();
        var items = Enumerable.Range(1, 100).Select(i => new AnimeEntity
        {
            Id = i,
            Title = $"Anime Title {i}",
            EnglishTitle = $"English Anime {i}",
            RussianTitle = $"Русское Аниме {i}",
            StartYear = 2020 + (i % 5)
        }).ToList();

        cache.BuildIndex(items);

        var tasks = new List<Task>();

        // Concurrent searchers
        for (int i = 0; i < 10; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 200; j++)
                {
                    var results = cache.Search($"anime title {idx * 10 + (j % 10) + 1}");
                    foreach (var match in results)
                    {
                        Assert.True(match.Id > 0);
                        Assert.True(match.Weight > 0);
                    }
                }
            }));
        }

        // Concurrent adders
        for (int i = 0; i < 5; i++)
        {
            int adderId = 1000 + i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    cache.AddMatch($"concurrent dynamic title {j}", adderId, 0.8f);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Verification after concurrent mutations
        var dynamicResults = cache.Search("concurrent dynamic title 50").ToList();
        Assert.NotEmpty(dynamicResults);
    }

    [Fact]
    public void AnimeEntityPresentation_SecondaryTitle_UsesCachedValue()
    {
        AnimeEntityPresentation.GetUseRussianTitles = () => false;

        var entity = new AnimeEntity
        {
            Title = "Attack on Titan",
            EnglishTitle = "Attack on Titan",
            RussianTitle = "Атака титанов"
        };

        // With English same as Title -> SecondaryTitle is null
        Assert.Null(entity.Presentation.SecondaryTitle);
        Assert.False(entity.Presentation.HasSecondaryTitle);

        // Switch setting to use Russian titles
        AnimeEntityPresentation.GetUseRussianTitles = () => true;
        Assert.Equal("Атака титанов", entity.Presentation.SecondaryTitle);
        Assert.True(entity.Presentation.HasSecondaryTitle);

        // Reset
        AnimeEntityPresentation.GetUseRussianTitles = () => false;
    }

    [Fact]
    public async Task SeasonalCacheStore_SaveAndLoad_RoundtripsCorrectly()
    {
        var store = new SeasonalCacheStore();
        int year = 2099;
        string season = "winter";

        var testItems = new List<AnimeEntity>
        {
            new()
            {
                Id = 99901,
                Title = "Test Seasonal Stream Anime",
                RussianTitle = "Тестовое Сезонное Аниме",
                Status = UserAnimeStatus.Watching,
                Rating = "pg_13",
                EpisodesAired = 6,
                TotalEpisodes = 12
            }
        };

        await store.SaveAsync(year, season, testItems);

        var loaded = store.LoadAll();
        var match = loaded.FirstOrDefault(x => x.Year == year && string.Equals(x.Season, "Winter", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(match.Items);
        Assert.Single(match.Items);
        Assert.Equal("Test Seasonal Stream Anime", match.Items[0].Title);
        Assert.Equal(6, match.Items[0].EpisodesAired);
    }
}
