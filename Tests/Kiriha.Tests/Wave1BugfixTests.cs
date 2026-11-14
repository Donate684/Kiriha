using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Kiriha.ViewModels.AnimeDetails;

namespace Kiriha.Tests;

public sealed class Wave1BugfixTests
{
    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public DelegatingHandlerStub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;
        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler) : this(req => Task.FromResult(handler(req))) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _handler(request);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new(_options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppDbContext(_options));
    }

    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());

        public Task InvokeAsync(Func<Task> action) => action();

        public void Post(Action action) => action();
    }

    private static (AppDbContext context, IDbContextFactory<AppDbContext> factory, string dbPath) CreateTestDb()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var context = new AppDbContext(options);
        context.Database.Migrate();

        return (context, factory, dbPath);
    }

    private static void CleanupTestDb(string dbPath)
    {
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Composite Key in Database & Repositories
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompositeKey_AnimeAndMangaWithSameId_CoexistInDatabaseWithoutCollision()
    {
        var (context, _, dbPath) = CreateTestDb();
        try
        {
            var anime = new AnimeEntity
            {
                Id = 100,
                MediaKind = MediaKind.Anime,
                Title = "Shared Title Anime",
                Status = UserAnimeStatus.Watching,
                Progress = 5,
                TotalEpisodes = 12
            };

            var manga = new AnimeEntity
            {
                Id = 100,
                MediaKind = MediaKind.Manga,
                Title = "Shared Title Manga",
                Status = UserAnimeStatus.PlanToWatch,
                ChaptersRead = 1,
                Chapters = 50
            };

            context.UserAnime.AddRange(anime, manga);
            await context.SaveChangesAsync();

            var loadedAnime = await context.UserAnime.FindAsync(100, MediaKind.Anime);
            var loadedManga = await context.UserAnime.FindAsync(100, MediaKind.Manga);

            Assert.NotNull(loadedAnime);
            Assert.NotNull(loadedManga);
            Assert.Equal("Shared Title Anime", loadedAnime.Title);
            Assert.Equal(MediaKind.Anime, loadedAnime.MediaKind);
            Assert.Equal("Shared Title Manga", loadedManga.Title);
            Assert.Equal(MediaKind.Manga, loadedManga.MediaKind);

            var all = await context.UserAnime.ToListAsync();
            Assert.Equal(2, all.Count);
        }
        finally
        {
            await context.DisposeAsync();
            CleanupTestDb(dbPath);
        }
    }

    [Fact]
    public async Task UserAnimeRepository_CompositeKey_CrudOperations_TargetCorrectKind()
    {
        var (context, factory, dbPath) = CreateTestDb();
        try
        {
            var repo = new UserAnimeRepository(factory);

            var anime = new AnimeEntity
            {
                Id = 200,
                MediaKind = MediaKind.Anime,
                Title = "Attack on Titan",
                Status = UserAnimeStatus.Watching,
                Progress = 3,
                TotalEpisodes = 25
            };

            var manga = new AnimeEntity
            {
                Id = 200,
                MediaKind = MediaKind.Manga,
                Title = "Attack on Titan (Manga)",
                Status = UserAnimeStatus.Watching,
                ChaptersRead = 10,
                Chapters = 139
            };

            await repo.UpsertAsync(anime);
            await repo.UpsertAsync(manga);

            // Verify isolated retrieval
            var fetchedAnime = await repo.GetByIdAsync(200, MediaKind.Anime);
            var fetchedManga = await repo.GetByIdAsync(200, MediaKind.Manga);

            Assert.NotNull(fetchedAnime);
            Assert.NotNull(fetchedManga);
            Assert.Equal("Attack on Titan", fetchedAnime.Title);
            Assert.Equal("Attack on Titan (Manga)", fetchedManga.Title);

            // Progress update for Anime should not affect Manga
            await repo.UpdateProgressAsync(anime, 15, UserAnimeStatus.Watching);

            fetchedAnime = await repo.GetByIdAsync(200, MediaKind.Anime);
            fetchedManga = await repo.GetByIdAsync(200, MediaKind.Manga);

            Assert.NotNull(fetchedAnime);
            Assert.NotNull(fetchedManga);
            Assert.Equal(15, fetchedAnime.Progress);
            Assert.Equal(10, fetchedManga.ChaptersRead);

            // Delete Anime: Manga must remain in database
            await repo.DeleteAsync(200, MediaKind.Anime);

            Assert.Null(await repo.GetByIdAsync(200, MediaKind.Anime));
            var remainingManga = await repo.GetByIdAsync(200, MediaKind.Manga);
            Assert.NotNull(remainingManga);
            Assert.Equal("Attack on Titan (Manga)", remainingManga.Title);
        }
        finally
        {
            await context.DisposeAsync();
            CleanupTestDb(dbPath);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. AniList ID Resolution: Null on failure & Permanent vs Transient
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAniListMediaIdAsync_ReturnsNullAndDifferentiatesPermanentVsTransient()
    {
        var mockCache = new Mock<IHttpCacheRepository>();
        var settings = new AppSettings();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var statusCodeToReturn = HttpStatusCode.NotFound;
        var contentToReturn = "{\"errors\":[{\"message\":\"Not Found.\",\"status\":404}]}";

        var handler = new DelegatingHandlerStub(req =>
        {
            return new HttpResponseMessage(statusCodeToReturn)
            {
                Content = new StringContent(contentToReturn, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        using var apiService = new AniListApiService(client, mockCache.Object, mockSettings.Object);

        // 1. Permanent Failure on 404
        statusCodeToReturn = HttpStatusCode.NotFound;
        contentToReturn = "{\"errors\":[{\"message\":\"Not Found.\",\"status\":404}]}";
        var (mediaId404, outcome404) = await apiService.ResolveAniListMediaIdAsync(12345, "ANIME", CancellationToken.None);
        Assert.Null(mediaId404);
        Assert.Equal(SyncOutcome.PermanentFailure, outcome404);

        // 2. Transient Failure on 500
        statusCodeToReturn = HttpStatusCode.InternalServerError;
        contentToReturn = "{\"errors\":[{\"message\":\"Internal server error.\",\"status\":500}]}";
        var (mediaId500, outcome500) = await apiService.ResolveAniListMediaIdAsync(12345, "ANIME", CancellationToken.None);
        Assert.Null(mediaId500);
        Assert.Equal(SyncOutcome.TransientFailure, outcome500);

        // 3. Success on 200 with media ID
        statusCodeToReturn = HttpStatusCode.OK;
        contentToReturn = "{\"data\":{\"Media\":{\"id\":887766}}}";
        var (mediaIdSuccess, outcomeSuccess) = await apiService.ResolveAniListMediaIdAsync(12345, "ANIME", CancellationToken.None);
        Assert.Equal(887766, mediaIdSuccess);
        Assert.Equal(SyncOutcome.Success, outcomeSuccess);
    }

    [Fact]
    public async Task AniListApiService_MalToAniListMap_DifferentiatesAnimeAndMangaWithSameMalId()
    {
        var mockCache = new Mock<IHttpCacheRepository>();
        var settings = new AppSettings();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var handler = new DelegatingHandlerStub(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            if (body.Contains("\"type\":\"ANIME\""))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":{\"Media\":{\"id\":111}}}", System.Text.Encoding.UTF8, "application/json")
                };
            }
            else
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":{\"Media\":{\"id\":222}}}", System.Text.Encoding.UTF8, "application/json")
                };
            }
        });

        using var client = new HttpClient(handler);
        using var apiService = new AniListApiService(client, mockCache.Object, mockSettings.Object);

        var (animeId, _) = await apiService.ResolveAniListMediaIdAsync(500, "ANIME", CancellationToken.None);
        var (mangaId, _) = await apiService.ResolveAniListMediaIdAsync(500, "MANGA", CancellationToken.None);

        Assert.Equal(111, animeId);
        Assert.Equal(222, mangaId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Preservation of Locally Enriched Fields
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UserAnimeRepository_SyncFromRemoteAsync_PreservesLocallyEnrichedFields()
    {
        var (context, factory, dbPath) = CreateTestDb();
        try
        {
            var repo = new UserAnimeRepository(factory);

            var nextEp = DateTime.UtcNow.AddDays(7);
            var lastSync = DateTime.UtcNow.AddHours(-1);

            var local = new AnimeEntity
            {
                Id = 300,
                MediaKind = MediaKind.Anime,
                Title = "Local Title",
                RussianTitle = "Русское название",
                RussianSynopsis = "Русский синопсис",
                LocalPosterPath = "C:/cache/posters/300.jpg",
                TotalEpisodes = 24,
                LastEpisodesSync = lastSync,
                NextEpisodeAt = nextEp,
                Status = UserAnimeStatus.Watching,
                Progress = 10
            };

            await repo.UpsertAsync(local);

            // Remote has empty local enrichments
            var remote = new AnimeEntity
            {
                Id = 300,
                MediaKind = MediaKind.Anime,
                Title = "Remote Title",
                RussianTitle = null,
                RussianSynopsis = null,
                LocalPosterPath = null,
                TotalEpisodes = 0,
                LastEpisodesSync = null,
                NextEpisodeAt = null,
                Status = UserAnimeStatus.Watching,
                Progress = 12
            };

            await repo.SyncFromRemoteAsync(new[] { remote }, [MediaKind.Anime], CancellationToken.None);

            var updated = await repo.GetByIdAsync(300, MediaKind.Anime);
            Assert.NotNull(updated);
            Assert.Equal("Русское название", updated.RussianTitle);
            Assert.Equal("Русский синопсис", updated.RussianSynopsis);
            Assert.Equal("C:/cache/posters/300.jpg", updated.LocalPosterPath);
            Assert.Equal(24, updated.TotalEpisodes);
            Assert.Equal(lastSync, updated.LastEpisodesSync);
            Assert.Equal(nextEp, updated.NextEpisodeAt);
            Assert.Equal(12, updated.Progress);
        }
        finally
        {
            await context.DisposeAsync();
            CleanupTestDb(dbPath);
        }
    }

    [Fact]
    public void AnimeEntity_CopyTo_PreservesLocallyEnrichedFields_WhenRemoteEmpty()
    {
        var nextEp = DateTime.UtcNow.AddDays(3);
        var lastSync = DateTime.UtcNow;

        var target = new AnimeEntity
        {
            Id = 400,
            Title = "Target Title",
            RussianTitle = "Локальный тайтл",
            RussianSynopsis = "Локальное описание",
            LocalPosterPath = "/local/path/poster.jpg",
            MainPictureUrl = "https://example.com/poster.jpg",
            TotalEpisodes = 12,
            LastEpisodesSync = lastSync,
            NextEpisodeAt = nextEp
        };

        var source = new AnimeEntity
        {
            Id = 400,
            Title = "Updated Remote Title",
            RussianTitle = null,
            RussianSynopsis = null,
            LocalPosterPath = null,
            MainPictureUrl = "https://example.com/new_poster.jpg",
            TotalEpisodes = 0,
            LastEpisodesSync = null,
            NextEpisodeAt = null
        };

        source.CopyTo(target);

        Assert.Equal("Updated Remote Title", target.Title);
        Assert.Equal("Локальный тайтл", target.RussianTitle);
        Assert.Equal("Локальное описание", target.RussianSynopsis);
        Assert.Equal("/local/path/poster.jpg", target.LocalPosterPath);
        Assert.Equal(12, target.TotalEpisodes);
        Assert.Equal(lastSync, target.LastEpisodesSync);
        Assert.Equal(nextEp, target.NextEpisodeAt);
    }

    [Fact]
    public async Task AnimeMetadataViewModel_LoadFullDetailsAsync_DoesNotOverwriteUserStatusScoreNotesOrDates()
    {
        var anime = new AnimeEntity
        {
            Id = 500,
            Title = "Frieren",
            Status = UserAnimeStatus.Watching,
            Progress = 5,
            Score = "8",
            Notes = "Keep my note intact",
            IsRewatching = true,
            DateStarted = new DateTime(2023, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            DateCompleted = null,
            Genres = new List<string>(),
            Studios = new List<string>()
        };

        var fullDetails = new AnimeEntity
        {
            Id = 500,
            Title = "Sousou no Frieren",
            Status = UserAnimeStatus.Completed,
            Progress = 28,
            Score = "10",
            Notes = "Mal server note that shouldn't wipe local",
            IsRewatching = false,
            DateStarted = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateCompleted = new DateTime(2024, 3, 22, 0, 0, 0, DateTimeKind.Utc),
            Genres = new List<string> { "Adventure", "Drama", "Fantasy" },
            Studios = new List<string> { "Madhouse" },
            Synopsis = "Frieren travels..."
        };

        var mockMal = new Mock<IMalApiService>();
        mockMal.Setup(m => m.GetAnimeDetailsAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullDetails);

        var vm = new AnimeMetadataViewModel(anime, mockMal.Object);
        await vm.LoadFullDetailsAsync();

        // User fields must be preserved
        Assert.Equal(UserAnimeStatus.Watching, anime.Status);
        Assert.Equal(5, anime.Progress);
        Assert.Equal("8", anime.Score);
        Assert.Equal("Keep my note intact", anime.Notes);
        Assert.True(anime.IsRewatching);
        Assert.Equal(new DateTime(2023, 10, 1, 0, 0, 0, DateTimeKind.Utc), anime.DateStarted);
        Assert.Null(anime.DateCompleted);

        // Metadata must be merged
        Assert.Equal(new[] { "Adventure", "Drama", "Fantasy" }, anime.Genres);
        Assert.Equal(new[] { "Madhouse" }, anime.Studios);
        Assert.Equal("Frieren travels...", anime.Synopsis);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Score Reset & Notes/Dates Clearing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnimeEntity_IsMissingDetails_DoesNotRequireStudiosForManga()
    {
        var mangaWithGenres = new AnimeEntity
        {
            MediaKind = MediaKind.Manga,
            StatusDetailed = "finished",
            Genres = new List<string> { "Action", "Supernatural" },
            Studios = new List<string>() // Manga never has studios
        };

        var mangaWithoutGenres = new AnimeEntity
        {
            MediaKind = MediaKind.Manga,
            StatusDetailed = "finished",
            Genres = new List<string>(),
            Studios = new List<string>()
        };

        var animeWithoutDetails = new AnimeEntity
        {
            MediaKind = MediaKind.Anime,
            StatusDetailed = "finished",
            Genres = new List<string>(),
            Studios = new List<string>() // Anime without genres & studios is missing details
        };

        Assert.False(mangaWithGenres.IsMissingDetails);
        Assert.True(mangaWithoutGenres.IsMissingDetails);
        Assert.True(animeWithoutDetails.IsMissingDetails);
    }

    [Fact]
    public async Task AnimeRepository_AddOrUpdateAnimeAsync_DoesNotRevertClearedScoreAndNotes()
    {
        var mockRepo = new Mock<IUserAnimeRepository>();
        var uiDispatcher = new SyncUiDispatcher();

        var repo = new AnimeRepository(mockRepo.Object, null!, null!, uiDispatcher, null!);

        var initial = new AnimeEntity
        {
            Id = 600,
            MediaKind = MediaKind.Anime,
            Title = "Steins;Gate",
            Status = UserAnimeStatus.Watching,
            Score = "9",
            Notes = "Loved the early episodes"
        };

        await repo.AddOrUpdateAnimeAsync(initial);

        var updated = new AnimeEntity
        {
            Id = 600,
            MediaKind = MediaKind.Anime,
            Title = "Steins;Gate",
            Status = UserAnimeStatus.Watching,
            Score = "-", // Cleared score
            Notes = ""   // Cleared notes
        };

        await repo.AddOrUpdateAnimeAsync(updated);

        var found = repo.Collection.Single(x => x.Id == 600 && x.MediaKind == MediaKind.Anime);
        Assert.Equal("-", found.Score);
        Assert.Equal("", found.Notes);
    }

    [Fact]
    public async Task ShikiApiService_SaveFullListStatusAsync_SendsZeroScoreWhenUnrated_AndIncludesNotes()
    {
        var mockCache = new Mock<IHttpCacheRepository>();
        var resolver = new ShikiHostResolver();
        var settings = new AppSettings();

        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            IsEnabled = true,
            Tokens = new ShikiTokens
            {
                AccessToken = "shiki_test_token",
                UserId = 1234,
                CreatedAt = DateTime.UtcNow,
                ExpiresIn = 86400
            }
        });

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        string? capturedBody = null;
        var handler = new DelegatingHandlerStub(async req =>
        {
            // For FindUserRateIdAsync query
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"id\":999,\"target_id\":700,\"target_type\":\"Anime\"}]", System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Patch || req.Method == HttpMethod.Put || req.Method == HttpMethod.Post)
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":999}", System.Text.Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var tokenService = new ShikiTokenService(mockSettings.Object, null!);
        var apiService = new ShikiApiService(
            client,
            mockSettings.Object,
            tokenService,
            resolver,
            mockCache.Object,
            null,
            ShikiMirror.One);

        var item = new AnimeEntity
        {
            Id = 700,
            MediaKind = MediaKind.Anime,
            Title = "Show",
            Status = UserAnimeStatus.Watching,
            Progress = 4,
            Score = "-",
            Notes = "Custom note preserved"
        };

        var outcome = await apiService.SaveFullListStatusAsync(item, CancellationToken.None);

        Assert.Equal(SyncOutcome.Success, outcome);
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var userRate = doc.RootElement.GetProperty("user_rate");
        Assert.Equal(0, userRate.GetProperty("score").GetInt32());
        Assert.Equal("Custom note preserved", userRate.GetProperty("text").GetString());
    }

    [Fact]
    public async Task MalApiService_SaveFullListStatusAsync_SendsZeroScoreWhenUnrated()
    {
        var settings = new AppSettings();
        settings.Api.Mal = new MalTokens { AccessToken = "test_mal_tok" };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var mockCache = new Mock<IHttpCacheRepository>();

        string? capturedForm = null;
        var handler = new DelegatingHandlerStub(async req =>
        {
            capturedForm = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"watching\"}", System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var authService = new MalAuthService(client);
        using var tokenManager = new MalTokenManager(mockSettings.Object, authService);
        using var malService = new MalApiService(client, mockSettings.Object, tokenManager, mockCache.Object);

        var item = new AnimeEntity
        {
            Id = 800,
            MediaKind = MediaKind.Anime,
            Title = "Show",
            Status = UserAnimeStatus.Watching,
            Progress = 5,
            Score = "-"
        };

        var outcome = await malService.SaveFullListStatusAsync(item, CancellationToken.None);

        Assert.Equal(SyncOutcome.Success, outcome);
        Assert.NotNull(capturedForm);
        Assert.Contains("score=0", capturedForm);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Shikimori Hardening
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShikiHttp_SendShikiAsync_PinsMirrorOnlyOnHttp2xx_NotOn403Or451Or502()
    {
        var resolver = new ShikiHostResolver();

        // 1. Initial 404 on shikimori.net
        // 2. Probe to shikimori.rip returns 403 Forbidden -> should NOT pin .rip
        // 3. Probe to shikimori.fi returns 200 OK -> SHOULD pin .fi
        var handler = new DelegatingHandlerStub(req =>
        {
            if (req.RequestUri!.Host == "shikimori.net")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (req.RequestUri.Host == "shikimori.rip")
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }
            if (req.RequestUri.Host == "shikimori.fi")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://shikimori.net/api/animes/123");

        var response = await ShikiHttp.SendShikiAsync(client, request, resolver, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("shikimori.fi", resolver.ActiveForkHost);
    }

    [Fact]
    public void ShikiApiService_BoundTrackerId_IsolatesCredentialsBetweenOrigAndFork()
    {
        var settings = new AppSettings();
        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            IsEnabled = true,
            Tokens = new ShikiTokens { AccessToken = "tok_orig", UserId = 111 }
        });
        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiFork,
            IsEnabled = true,
            Tokens = new ShikiTokens { AccessToken = "tok_fork", UserId = 222 }
        });

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        using var client = new HttpClient();
        var mockCache = new Mock<IHttpCacheRepository>();
        var resolver = new ShikiHostResolver();
        var tokenService = new ShikiTokenService(mockSettings.Object, null!);

        var serviceOrig = new ShikiApiService(
            client,
            mockSettings.Object,
            tokenService,
            resolver,
            mockCache.Object,
            null,
            ShikiMirror.One);

        var serviceFork = new ShikiApiService(
            client,
            mockSettings.Object,
            tokenService,
            resolver,
            mockCache.Object,
            null,
            ShikiMirror.Net);

        Assert.Equal(TrackerConstants.Ids.ShikiOrig, serviceOrig.TrackerId);
        Assert.Equal(TrackerConstants.Ids.ShikiFork, serviceFork.TrackerId);

        var origAccount = settings.Api.GetAccount(serviceOrig.TrackerId);
        var forkAccount = settings.Api.GetAccount(serviceFork.TrackerId);

        Assert.NotNull(origAccount);
        Assert.NotNull(forkAccount);
        var origTokens = origAccount.Tokens as ShikiTokens;
        var forkTokens = forkAccount.Tokens as ShikiTokens;
        Assert.NotNull(origTokens);
        Assert.NotNull(forkTokens);
        Assert.Equal(111, origTokens.UserId);
        Assert.Equal(222, forkTokens.UserId);
        Assert.Equal("tok_orig", origTokens.AccessToken);
        Assert.Equal("tok_fork", forkTokens.AccessToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Status Mapping & Rewatch Completion Transitions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StatusMapper_ToAniList_ReturnsCompleted_EvenIfIsRewatchingIsTrue()
    {
        // When finished, COMPLETED must take precedence over REPEATING
        Assert.Equal("COMPLETED", StatusMapper.ToAniList(UserAnimeStatus.Completed, isRewatching: true));
        Assert.Equal("REPEATING", StatusMapper.ToAniList(UserAnimeStatus.Watching, isRewatching: true));
    }

    [Fact]
    public async Task AnimeProgressService_SmartIncrementProgressAsync_ResetsIsRewatchingOnCompletion()
    {
        var mockRepo = new Mock<IAnimeRepository>();
        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockSync = new Mock<ISyncManager>();
        var mockHistory = new Mock<IHistoryService>();
        var uiDispatcher = new SyncUiDispatcher();

        var service = new AnimeProgressService(
            mockRepo.Object,
            mockUserRepo.Object,
            mockSync.Object,
            mockHistory.Object,
            uiDispatcher);

        var item = new AnimeEntity
        {
            Id = 900,
            MediaKind = MediaKind.Anime,
            Title = "Finished Show",
            Status = UserAnimeStatus.Watching,
            Progress = 11,
            TotalEpisodes = 12,
            IsRewatching = true
        };

        var nextStatus = await service.SmartIncrementProgressAsync(item, 12);

        Assert.Equal(UserAnimeStatus.Completed, nextStatus);
        Assert.Equal(UserAnimeStatus.Completed, item.Status);
        Assert.False(item.IsRewatching);
    }

    [Fact]
    public void AniListMapper_MapsMediaToAnimeEntity_DoesNotClampDayTo28()
    {
        const string json = """
        {
          "id": 9999,
          "idMal": 9999,
          "title": { "english": "Summer Finale" },
          "format": "TV",
          "startDate": { "year": 2023, "month": 8, "day": 31 }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var entity = AniListMapper.MapMediaToAnimeEntity(doc.RootElement);

        Assert.NotNull(entity.AiringDate);
        Assert.Equal(new DateTime(2023, 8, 31, 0, 0, 0, DateTimeKind.Utc), entity.AiringDate);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Concurrent Token Refresh Deduplication
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MalTokenManager_ConcurrentCalls_ExecuteRefreshOnlyOnce()
    {
        var settings = new AppSettings();
        settings.Api.Mal = new MalTokens
        {
            AccessToken = "old_stale_access_token",
            RefreshToken = "valid_refresh_token",
            ExpiresIn = 0,
            CreatedAt = DateTime.UtcNow.AddHours(-5) // Expired
        };

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);
        mockSettings.Setup(s => s.Update(It.IsAny<Action<AppSettings>>(), It.IsAny<bool>()))
            .Callback<Action<AppSettings>, bool>((action, _) => action(settings));

        int refreshCount = 0;
        var handler = new DelegatingHandlerStub(async req =>
        {
            Interlocked.Increment(ref refreshCount);
            // Simulate network latency so concurrent callers queue behind the lock
            await Task.Delay(50);
            var responseBody = """
            {
                "access_token": "new_refreshed_access_token",
                "refresh_token": "new_refreshed_token",
                "expires_in": 3600
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var authService = new MalAuthService(client);
        using var tokenManager = new MalTokenManager(mockSettings.Object, authService);

        // 5 concurrent requests with stale failedToken
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => tokenManager.EnsureValidTokenAsync(CancellationToken.None, forceRefresh: true, failedToken: "old_stale_access_token"))
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        // All callers receive the new token
        foreach (var tok in tokens)
        {
            Assert.Equal("new_refreshed_access_token", tok);
        }

        // But RefreshTokenAsync was only invoked once
        Assert.Equal(1, refreshCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Adversarial Regression Fixes (Iteration 2)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnimeSyncOrchestrator_ProcessSyncResults_DoesNotSkipAnimeWhenMangaWithSameIdWasDeleted()
    {
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        mockAnimeRepo.Setup(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>()))
            .ReturnsAsync(new List<AnimeEntity>());
        mockAnimeRepo.Setup(r => r.GetSnapshotAsync())
            .ReturnsAsync(new List<AnimeEntity>());

        // Simulate Manga 21 was recently deleted; Anime 21 is NOT deleted
        mockAnimeRepo.Setup(r => r.IsRecentlyDeleted(21, MediaKind.Manga)).Returns(true);
        mockAnimeRepo.Setup(r => r.IsRecentlyDeleted(21, MediaKind.Anime)).Returns(false);
        // If the single-arg IsRecentlyDeleted(21) is called, it would return true (cross-kind collision bug)
        mockAnimeRepo.Setup(r => r.IsRecentlyDeleted(21)).Returns(true);

        var addedItems = new List<AnimeEntity>();
        mockAnimeRepo.Setup(r => r.AddToCollection(It.IsAny<AnimeEntity>()))
            .Callback<AnimeEntity>(addedItems.Add);

        mockAnimeRepo.Setup(r => r.ApplySyncBatchAsync(It.IsAny<List<AnimeEntity>>(), It.IsAny<List<Action>>()))
            .Callback<List<AnimeEntity>, List<Action>>((_, actions) =>
            {
                foreach (var action in actions) action();
            })
            .Returns(Task.CompletedTask);

        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockRecognitionCache = new Mock<IRecognitionCache>();

        var incomingAnime = new AnimeEntity
        {
            Id = 21,
            MediaKind = MediaKind.Anime,
            Title = "One Piece Anime",
            Status = UserAnimeStatus.Watching,
            Progress = 1000
        };

        var mockTracker = new Mock<ITrackerService>();
        mockTracker.Setup(t => t.Name).Returns("MockTracker");
        mockTracker.Setup(t => t.TrackerId).Returns(TrackerConstants.Ids.Mal);
        mockTracker.Setup(t => t.IsEnabled).Returns(true);
        mockTracker.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AnimeEntity> { incomingAnime });

        var settings = new AppSettings();
        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            IsEnabled = true,
            IsPrimary = true
        });
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var orchestrator = new AnimeSyncOrchestrator(
            mockAnimeRepo.Object,
            mockUserRepo.Object,
            new[] { mockTracker.Object },
            mockRecognitionCache.Object,
            mockSettings.Object);

        var result = await orchestrator.SyncWithTrackersAsync();

        Assert.True(result);
        // Verify IsRecentlyDeleted was queried with the entity's MediaKind (Anime), which returned false
        mockAnimeRepo.Verify(r => r.IsRecentlyDeleted(21, MediaKind.Anime), Times.Once);
        // Anime ID 21 was NOT skipped and was added to the collection
        Assert.Single(addedItems);
        Assert.Equal(21, addedItems[0].Id);
        Assert.Equal(MediaKind.Anime, addedItems[0].MediaKind);
    }

    [Fact]
    public async Task AniListApiService_SaveFullListStatusAsync_Manga_RoutesToUpdateMangaProgressWithChaptersAndMangaMediaResolution()
    {
        var mockCache = new Mock<IHttpCacheRepository>();
        var settings = new AppSettings();
        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.AniList,
            IsEnabled = true,
            Tokens = new AniListTokens { AccessToken = "valid_anilist_token" }
        });
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var requests = new List<string>();
        var handler = new DelegatingHandlerStub(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            requests.Add(body);

            if (body.Contains("query ($malId: Int, $type: MediaType)"))
            {
                // Verify that ResolveAniListMediaIdAsync resolves MANGA, not ANIME
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":{\"Media\":{\"id\":5555}}}", System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (body.Contains("SaveMediaListEntry"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":{\"SaveMediaListEntry\":{\"id\":999,\"progress\":45,\"status\":\"CURRENT\"}}}", System.Text.Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"errors\":[{\"message\":\"Unexpected query\"}]}", System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        using var apiService = new AniListApiService(client, mockCache.Object, mockSettings.Object);

        var manga = new AnimeEntity
        {
            Id = 21,
            MediaKind = MediaKind.Manga,
            Title = "Death Note Manga",
            ChaptersRead = 45,
            VolumesRead = 4,
            Progress = 0,
            Score = "9",
            Status = UserAnimeStatus.Watching
        };

        var outcome = await apiService.SaveFullListStatusAsync(manga, CancellationToken.None);

        Assert.Equal(SyncOutcome.Success, outcome);
        Assert.Equal(2, requests.Count);

        // First request: ResolveAniListMediaIdAsync resolving with MANGA type
        Assert.Contains("\"type\":\"MANGA\"", requests[0]);
        Assert.DoesNotContain("\"type\":\"ANIME\"", requests[0]);

        // Second request: SaveMediaListEntry mutation with chapters, volumes, and score
        Assert.Contains("\"progress\":45", requests[1]);
        Assert.Contains("\"progressVolumes\":4", requests[1]);
        Assert.Contains("\"score\":9", requests[1]);
    }
}
