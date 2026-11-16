using Kiriha.Core.Tracking.Mapping;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed class ManualMappingServiceTests
{
    [Fact]
    public async Task AddMapping_NormalizesTitleAndPersistsToSqlite()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var repo = await CreateRepoAsync(conn);

        var service = new ManualMappingService(repo);
        service.AddMapping("Sousou no Frieren", 52991);
        await service.FlushAsync();

        var reloaded = new ManualMappingService(repo);
        Assert.True(reloaded.TryGetMapping("sousou no frieren", out var malId));
        Assert.Equal(52991, malId);
    }

    [Fact]
    public async Task AddNegativeMapping_PreventsPositiveLookupAndSurvivesReload()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var repo = await CreateRepoAsync(conn);

        var service = new ManualMappingService(repo);
        service.AddNegativeMapping("Wrong Window Title");
        await service.FlushAsync();

        var reloaded = new ManualMappingService(repo);
        Assert.True(reloaded.IsNegativelyMapped("wrong window title"));
        Assert.False(reloaded.TryGetMapping("wrong window title", out _));
    }

    [Fact]
    public async Task RemoveMapping_ReturnsTrueOnlyWhenEntryExisted()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var repo = await CreateRepoAsync(conn);

        var service = new ManualMappingService(repo);
        service.AddMapping("Cowboy Bebop", 1);
        await service.FlushAsync();

        Assert.True(service.RemoveMapping("Cowboy Bebop"));
        Assert.False(service.RemoveMapping("Cowboy Bebop"));
        await service.FlushAsync();

        var reloaded = new ManualMappingService(repo);
        Assert.False(reloaded.TryGetMapping("cowboy bebop", out _));
    }

    [Fact]
    public void BlankTitlesAreIgnored()
    {
        var service = new ManualMappingService();

        service.AddMapping(" ", 1);
        service.AddNegativeMapping("");

        Assert.False(service.TryGetMapping("", out _));
        Assert.False(service.IsNegativelyMapped(""));
    }

    private static async Task<TitleMappingRepository> CreateRepoAsync(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var mockFactory = new Mock<IDbContextFactory<AppDbContext>>();
        mockFactory.Setup(f => f.CreateDbContext()).Returns(() => new AppDbContext(options));
        mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(options));

        return new TitleMappingRepository(mockFactory.Object);
    }
}
