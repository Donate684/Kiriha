using Kiriha.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Streaming.Helpers;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Kiriha.Utils.Parsing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed class CoreAuditWaveFixesTests
{
    [Fact]
    public void KirihaData_DoesNotReference_KirihaInfrastructure()
    {
        var dataAssembly = typeof(AppDbContext).Assembly;
        var referencedNames = dataAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("Kiriha.Infrastructure", referencedNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kiriha.Core.Tracking", referencedNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PureParsers_ResideIn_CoreDomain_WithZeroProjectDependencies()
    {
        var domainAssembly = typeof(AnimeEntity).Assembly;
        Assert.Same(domainAssembly, typeof(AnimeReleaseParser).Assembly);
        Assert.Same(domainAssembly, typeof(AnimeStringHelper).Assembly);
        Assert.Same(domainAssembly, typeof(AnimeLibraryMatcher).Assembly);

        var kirihaRefs = domainAssembly.GetReferencedAssemblies()
            .Where(a => a.Name != null && a.Name.StartsWith("Kiriha", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(kirihaRefs);
    }

    [Fact]
    public void PlayerTorrentFormatting_FormatsSpeedsAndByteSizesCorrectly()
    {
        Assert.Equal("0 KB/s", PlayerViewModel.FormatSpeed(0));
        Assert.Equal("0.5 KB/s", PlayerViewModel.FormatSpeed(512));
        Assert.Equal("1.5 KB/s", PlayerViewModel.FormatSpeed(1536));
        Assert.Equal("2.5 MB/s", PlayerViewModel.FormatSpeed(2.5 * 1024 * 1024));

        Assert.Equal("0 MB", PlayerViewModel.FormatBytes(0));
        Assert.Equal("1.5 MB", PlayerViewModel.FormatBytes((long)(1.5 * 1024 * 1024)));
        Assert.Equal("1.25 GB", PlayerViewModel.FormatBytes((long)(1.25 * 1024 * 1024 * 1024)));
    }

    [Fact]
    public void ApiConfig_DisabledAccount_DoesNotFallbackToLegacyTokens()
    {
        var api = new AppSettings.ApiConfig
        {
            LegacyMal = new MalTokens { AccessToken = "legacy-mal-token" },
            LegacyShiki = new ShikiTokens { AccessToken = "legacy-shiki-token", Mirror = ShikiMirror.One }
        };

        api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            IsEnabled = false,
            Tokens = new MalTokens { AccessToken = "account-mal-token" }
        });
        api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            IsEnabled = false,
            Tokens = new ShikiTokens { AccessToken = "account-shiki-token", Mirror = ShikiMirror.One }
        });

        api.SyncLegacyFromAccounts();

        Assert.Null(api.Mal);
        Assert.Null(api.Shiki);
        Assert.Null(api.LegacyMal);
        Assert.Null(api.LegacyShiki);
    }

    [Fact]
    public void UrlRedactor_RedactsAccessTokensWhileKeepingSafeParameters()
    {
        var raw = "https://smotret-anime.com/api/translations/123?access_token=secret12345&fields=stream";
        var redacted = UrlRedactor.Redact(raw);

        Assert.DoesNotContain("secret12345", redacted, StringComparison.Ordinal);
        Assert.Contains("access_token=[REDACTED]", redacted, StringComparison.Ordinal);
        Assert.Contains("fields=stream", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void EmberTitleResolver_SkipsStreamUrls_WithoutDiskIo()
    {
        EmberTitleResolver.ClearCache();
        Assert.False(EmberTitleResolver.IsEmberRelease("https://stream.example.com/[Ember] Frieren - 01.m3u8?token=abc"));
        Assert.False(EmberTitleResolver.TryResolveDirectoryMetadata(
            "https://stream.example.com/[Ember] Frieren - 01.m3u8?token=abc",
            out _,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void AnimeCollectionProjection_MultiKindQuery_CombinesMangaAndLightNovel()
    {
        using var projection = new AnimeCollectionProjection();
        var anime = new AnimeEntity { Id = 1, MediaKind = MediaKind.Anime, Title = "Anime", Status = UserAnimeStatus.Watching };
        var manga = new AnimeEntity { Id = 2, MediaKind = MediaKind.Manga, Title = "Berserk", Status = UserAnimeStatus.Watching };
        var novel = new AnimeEntity { Id = 3, MediaKind = MediaKind.LightNovel, Title = "Eighty-Six", Status = UserAnimeStatus.Watching };

        projection.Rebuild([anime, manga, novel]);

        MediaKind[] readingKinds = [MediaKind.Manga, MediaKind.LightNovel];
        var counts = projection.CountAllStatuses(null, null, null, filterNsfw: false, readingKinds);
        Assert.Equal(2, counts.Watching);

        var items = projection.Query(UserAnimeStatus.Watching, null, null, null, filterNsfw: false, sortBy: AppConstants.Sorting.Title, readingKinds);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, x => x.MediaKind == MediaKind.Manga && x.Title == "Berserk");
        Assert.Contains(items, x => x.MediaKind == MediaKind.LightNovel && x.Title == "Eighty-Six");
    }

    [Fact]
    public async Task DatabaseMaintenance_CleanOrphanedMetadata_PreservesMangaAndLightNovelBitMaskedEntries()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(options));

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.UserAnime.AddRange(
                new AnimeEntity { Id = 10, MediaKind = MediaKind.Anime, Title = "Anime 10", Status = UserAnimeStatus.Watching },
                new AnimeEntity { Id = 20, MediaKind = MediaKind.Manga, Title = "Manga 20", Status = UserAnimeStatus.Watching },
                new AnimeEntity { Id = 30, MediaKind = MediaKind.LightNovel, Title = "Novel 30", Status = UserAnimeStatus.Watching });

            setup.Metadata.AddRange(
                new ShikiMetadata { Id = ShikiMetadata.GetCacheId(10, MediaKind.Anime), Russian = "Anime Ru", PosterUrl = "https://cdn.example/a.jpg" },
                new ShikiMetadata { Id = ShikiMetadata.GetCacheId(20, MediaKind.Manga), Russian = "Manga Ru", PosterUrl = "" },
                new ShikiMetadata { Id = ShikiMetadata.GetCacheId(30, MediaKind.LightNovel), Russian = "Novel Ru", PosterUrl = "" },
                new ShikiMetadata { Id = ShikiMetadata.GetCacheId(999, MediaKind.Manga), Russian = "Orphaned Manga" });
            await setup.SaveChangesAsync();
        }

        var maintenance = new DatabaseMaintenance(factoryMock.Object);
        await maintenance.PerformAsync();

        await using var verify = new AppDbContext(options);
        var survivingIds = await verify.Metadata.Select(m => m.Id).ToListAsync();

        Assert.Contains(ShikiMetadata.GetCacheId(10, MediaKind.Anime), survivingIds);
        Assert.Contains(ShikiMetadata.GetCacheId(20, MediaKind.Manga), survivingIds);
        Assert.Contains(ShikiMetadata.GetCacheId(30, MediaKind.LightNovel), survivingIds);
        Assert.DoesNotContain(ShikiMetadata.GetCacheId(999, MediaKind.Manga), survivingIds);

        var persistedAnimeMeta = await verify.Metadata.AsNoTracking().SingleAsync(m => m.Id == 10);
        Assert.Equal("https://cdn.example/a.jpg", persistedAnimeMeta.PosterUrl);
    }

    [Fact]
    public async Task HistoryRepository_ConcurrentTrackerUpdates_PreserveAllTrackerStates_AndFilterByMediaKind()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(options));

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var repo = new HistoryRepository(factoryMock.Object);
        await repo.AddAsync(new HistoryItem
        {
            AnimeId = 500,
            MediaKind = MediaKind.Anime,
            AnimeTitle = "Same ID Anime",
            Episode = 3,
            Timestamp = DateTime.UtcNow,
            ActionType = (int)HistoryActionType.Watched
        });
        await repo.AddAsync(new HistoryItem
        {
            AnimeId = 500,
            MediaKind = MediaKind.Manga,
            AnimeTitle = "Same ID Manga",
            Episode = 3,
            Timestamp = DateTime.UtcNow.AddSeconds(1),
            ActionType = (int)HistoryActionType.Watched
        });

        // Concurrent updates for Anime ID=500 must serialize without overwriting each other and must NOT touch Manga ID=500
        await Task.WhenAll(
            repo.UpdateTrackerStatusAsync(500, MediaKind.Anime, 3, "MyAnimeList", TrackerSyncState.Success),
            repo.UpdateTrackerStatusAsync(500, MediaKind.Anime, 3, "Shikimori", TrackerSyncState.Success),
            repo.UpdateTrackerStatusAsync(500, MediaKind.Anime, 3, "AniList", TrackerSyncState.Failed, "404"));

        var items = await repo.GetAsync();
        var animeEntry = items.Single(x => x.MediaKind == MediaKind.Anime);
        var mangaEntry = items.Single(x => x.MediaKind == MediaKind.Manga);

        Assert.Equal(TrackerSyncState.Success, animeEntry.TrackerStatuses["MyAnimeList"].State);
        Assert.Equal(TrackerSyncState.Success, animeEntry.TrackerStatuses["Shikimori"].State);
        Assert.Equal(TrackerSyncState.Failed, animeEntry.TrackerStatuses["AniList"].State);
        Assert.Empty(mangaEntry.TrackerStatuses);
    }

    [Fact]
    public void AnimeEntityPresentation_RespectsRussianDisplayTitleAndDescriptionDelegates()
    {
        var item = new AnimeEntity
        {
            Id = 1,
            Title = "Sousou no Frieren",
            RussianTitle = "Frieren RU",
            Synopsis = "English description",
            RussianSynopsis = "Russian description"
        };

        var pDisabled = new AnimeEntityPresentation(
            item,
            localizer: null,
            clock: null,
            getUseRussianTitles: () => false,
            getPreferRussianDisplayTitle: () => false,
            getUseRussianDescriptions: () => false);

        Assert.Equal("Sousou no Frieren", pDisabled.DisplayTitle);
        Assert.Equal("English description", pDisabled.DisplaySynopsis);

        var pEnabled = new AnimeEntityPresentation(
            item,
            localizer: null,
            clock: null,
            getUseRussianTitles: () => true,
            getPreferRussianDisplayTitle: () => true,
            getUseRussianDescriptions: () => true);

        Assert.Equal("Frieren RU", pEnabled.DisplayTitle);
        Assert.Equal("Russian description", pEnabled.DisplaySynopsis);
    }
}
