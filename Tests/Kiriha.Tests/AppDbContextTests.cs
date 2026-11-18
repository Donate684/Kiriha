using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Tests;

public sealed class AppDbContextTests
{
    [Fact]
    public async Task UserAnime_RoundTripsStatusAndJsonCollectionsThroughSqlite()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        try
        {
            await using (var context = new AppDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.UserAnime.Add(new AnimeEntity
                {
                    Id = 42,
                    Title = "Test Title",
                    Status = UserAnimeStatus.PlanToWatch,
                    Genres = new List<string> { "Drama", "Fantasy" },
                    Studios = new List<string> { "Studio A" },
                    AlternativeTitles = new List<string> { "Alt A", "Alt B" }
                });
                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(options))
            {
                var item = await context.UserAnime.SingleAsync(x => x.Id == 42);

                Assert.Equal(UserAnimeStatus.PlanToWatch, item.Status);
                Assert.Equal(new[] { "Drama", "Fantasy" }, item.Genres);
                Assert.Equal(new[] { "Studio A" }, item.Studios);
                Assert.Equal(new[] { "Alt A", "Alt B" }, item.AlternativeTitles);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task UserAnime_RemoveDisconnectedEntity_DoesNotThrow()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        try
        {
            await using (var context = new AppDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.UserAnime.Add(new AnimeEntity
                {
                    Id = 42,
                    Title = "Test Title",
                    Status = UserAnimeStatus.PlanToWatch,
                    MediaKind = MediaKind.Anime
                });
                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(options))
            {
                var existing = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == 42 && x.MediaKind == MediaKind.Anime);
                Assert.NotNull(existing);
                context.UserAnime.Remove(existing);
                await context.SaveChangesAsync();

                var deleted = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == 42 && x.MediaKind == MediaKind.Anime);
                Assert.Null(deleted);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task ShikiMetadata_RemoveDisconnectedEntity_DoesNotThrow()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        try
        {
            await using (var context = new AppDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.Metadata.Add(new Kiriha.Core.Domain.Models.Api.ShikiMetadata
                {
                    Id = 42,
                    MediaKind = MediaKind.Anime,
                    Name = "Test Meta",
                    Russian = "Тест"
                });
                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(options))
            {
                var existing = await context.Metadata.FirstOrDefaultAsync(x => x.Id == 42 && x.MediaKind == MediaKind.Anime);
                Assert.NotNull(existing);
                context.Metadata.Remove(existing);
                await context.SaveChangesAsync();

                var deleted = await context.Metadata.FirstOrDefaultAsync(x => x.Id == 42 && x.MediaKind == MediaKind.Anime);
                Assert.Null(deleted);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new(_options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppDbContext(_options));
    }

    [Fact]
    public async Task UserAnimeRepository_DeleteAsync_DeletesRecordFromDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        var factory = new TestDbContextFactory(options);

        try
        {
            await using (var context = await factory.CreateDbContextAsync())
            {
                await context.Database.EnsureCreatedAsync();
                context.UserAnime.Add(new AnimeEntity
                {
                    Id = 999,
                    Title = "Entity to Delete",
                    Status = UserAnimeStatus.Watching,
                    MediaKind = MediaKind.Anime
                });
                await context.SaveChangesAsync();
            }

            var repo = new Kiriha.Data.Repository.UserAnimeRepository(factory);
            await repo.DeleteAsync(999, MediaKind.Anime);

            await using (var verifyContext = await factory.CreateDbContextAsync())
            {
                var item = await verifyContext.UserAnime.FirstOrDefaultAsync(x => x.Id == 999 && x.MediaKind == MediaKind.Anime);
                Assert.Null(item);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }
}
