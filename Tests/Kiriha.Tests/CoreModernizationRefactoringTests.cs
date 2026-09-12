using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Extensions;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public sealed class CoreModernizationRefactoringTests
{
    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    [Fact]
    public void UppercaseFirst_HandlesVariousInputsCorrectly()
    {
        Assert.Equal(string.Empty, "".UppercaseFirst());
        Assert.Null(((string?)null).UppercaseFirst());
        Assert.Equal("A", "a".UppercaseFirst());
        Assert.Equal("A", "A".UppercaseFirst());
        Assert.Equal("Spring", "spring".UppercaseFirst());
        Assert.Equal("Winter", "Winter".UppercaseFirst());
        Assert.Equal("1st season", "1st season".UppercaseFirst());
        Assert.Equal("Атака", "атака".UppercaseFirst());
    }

    [Fact]
    public void AnimeEntityPresentation_WithInjectedDependencies_IsFullyIsolated()
    {
        var baseTime = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        var clock = new TestTimeProvider(baseTime);

        var mockLocalizer = new Mock<ILocalizer>();
        mockLocalizer.Setup(l => l.GetLoc("anime.labels.new_ep", It.IsAny<object?[]>()))
            .Returns("НОВАЯ СЕРИЯ");

        var item = new AnimeEntity
        {
            Id = 100,
            Title = "Sousou no Frieren",
            RussianTitle = "Провожающая в последний путь Фрирен",
            EnglishTitle = "Frieren: Beyond Journey's End",
            Status = UserAnimeStatus.Watching,
            Progress = 10,
            EpisodesAired = 11,
            LastEpisodeAt = baseTime.AddHours(-10)
        };

        // Presentation with Russian titles enabled
        var presRu = new AnimeEntityPresentation(item, mockLocalizer.Object, clock, () => true);
        Assert.Equal("Провожающая в последний путь Фрирен", presRu.SecondaryTitle);
        Assert.Equal("НОВАЯ СЕРИЯ", presRu.AiringBadgeText);

        // Presentation with Russian titles disabled - zero mutation of global static state
        var presEn = new AnimeEntityPresentation(item, mockLocalizer.Object, clock, () => false);
        Assert.Equal("Frieren: Beyond Journey's End", presEn.SecondaryTitle);
        Assert.Equal("НОВАЯ СЕРИЯ", presEn.AiringBadgeText);
    }

    [Fact]
    public async Task HttpCacheRepository_WithTimeProvider_RespectsTtl()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", $"http_cache_{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var factory = new TestDbContextFactory(options);
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var repo = new HttpCacheRepository(factory, clock);

        var sampleBody = "test content"u8.ToArray();
        await repo.UpsertAsync("hash123", "etag1", null, sampleBody);

        // Cache is valid immediately
        var entry = await repo.GetAsync("hash123");
        Assert.NotNull(entry);
        Assert.Equal(sampleBody, entry.Body);

        // Advance 20 days -> still within 30-day TTL
        clock.Advance(TimeSpan.FromDays(20));
        entry = await repo.GetAsync("hash123");
        Assert.NotNull(entry);

        // Advance another 15 days (total 35 days) -> exceeds 30-day TTL, must return null
        clock.Advance(TimeSpan.FromDays(15));
        entry = await repo.GetAsync("hash123");
        Assert.Null(entry);

        try { File.Delete(dbPath); } catch { }
    }

    [Fact]
    public async Task MalSearchCacheRepository_WithTimeProvider_RespectsTtl()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", $"mal_search_{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var factory = new TestDbContextFactory(options);
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var repo = new MalSearchCacheRepository(factory, clock);

        // Positive cache (animeId > 0, 30 days TTL)
        await repo.UpsertAsync("naruto", 20, 1.0f);
        // Negative cache (animeId = 0, 7 days TTL)
        await repo.UpsertAsync("nonexistent", 0, 0f);

        // Valid immediately
        Assert.NotNull(await repo.GetAsync("naruto"));
        Assert.NotNull(await repo.GetAsync("nonexistent"));

        // Advance 8 days -> negative cache expires, positive cache remains
        clock.Advance(TimeSpan.FromDays(8));
        Assert.NotNull(await repo.GetAsync("naruto"));
        Assert.Null(await repo.GetAsync("nonexistent"));

        // Advance 25 days more (total 33 days) -> positive cache also expires
        clock.Advance(TimeSpan.FromDays(25));
        Assert.Null(await repo.GetAsync("naruto"));

        try { File.Delete(dbPath); } catch { }
    }

    [Theory]
    [InlineData("finished_airing", true)]
    [InlineData("finished airing", true)]
    [InlineData("FINISHED_AIRING", true)]
    [InlineData("Finished Airing", true)]
    [InlineData("currently_airing", false)]
    [InlineData("not_yet_aired", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void AiringStatus_IsFinishedAiring_ValidatesCorrectly(string? status, bool expected)
    {
        Assert.Equal(expected, Kiriha.Core.Domain.Constants.AppConstants.AiringStatus.IsFinishedAiring(status));
    }

    [Fact]
    public void UIUtils_GetLoc_WithFormatArgs_FormatsCorrectly()
    {
        var formatted = Kiriha.Core.UIUtils.GetLoc("Episodes {0} of {1}", 12, 24);
        Assert.Equal("Episodes 12 of 24", formatted);
    }
}
