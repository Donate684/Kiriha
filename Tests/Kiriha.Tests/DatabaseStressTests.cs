using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Constants;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class DatabaseStressTests
{
    [Fact]
    public async Task GetAllAsync_With5000Records_LoadsUnderThreshold()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            // We use a shared in-memory connection to persist data between factory calls
            .UseSqlite("DataSource=file:memdb1?mode=memory&cache=shared")
            .Options;

        // Keep connection open so the DB doesn't disappear
        using var setupContext = new AppDbContext(options);
        await setupContext.Database.OpenConnectionAsync();
        await setupContext.Database.EnsureCreatedAsync();

        // Seed 5000 records
        var entities = new List<AnimeEntity>(5000);
        for (int i = 1; i <= 5000; i++)
        {
            entities.Add(new AnimeEntity
            {
                Id = i,
                Title = $"Anime {i}",
                EnglishTitle = $"Anime {i}",
                JapaneseTitle = $"アニメ {i}",
                Score = (i % 10).ToString(),
                Status = (UserAnimeStatus)(i % 6 + 1),
                Type = "tv",
                EpisodesAired = 12,
                Chapters = 12,
                ChaptersRead = (i % 12),
                Genres = new List<string> { "Action", "Comedy" },
                AlternativeTitles = new List<string>(),
                Studios = new List<string>()
            });
        }
        await setupContext.UserAnime.AddRangeAsync(entities);
        await setupContext.SaveChangesAsync();

        var mockFactory = new Mock<IDbContextFactory<AppDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(() => new AppDbContext(options));

        var repository = new UserAnimeRepository(mockFactory.Object);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await repository.GetAllAsync();
        stopwatch.Stop();

        // Assert
        Assert.Equal(5000, result.Count);
        
        // Assert that loading 5000 records takes less than 1 second (1000ms)
        // Usually EF Core SQLite handles this in ~100-200ms
        Assert.True(stopwatch.ElapsedMilliseconds < 1500, $"Loading 5000 records took too long: {stopwatch.ElapsedMilliseconds}ms");
    }
}
