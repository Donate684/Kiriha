using System.Reflection;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Parsing;
using Kiriha.Core.Tracking;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Mapping;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed class CoreRefactoringWave4Tests
{
    [Fact]
    public void KirihaCoreTracking_HasZeroMvvmReference_And_CleanContracts()
    {
        var trackingAssembly = typeof(TrackingServicesRegistration).Assembly;
        var referencedNames = trackingAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("CommunityToolkit.Mvvm", referencedNames);
        Assert.DoesNotContain("Kiriha.Infrastructure", referencedNames);
        Assert.DoesNotContain("Kiriha.Data", referencedNames);
        Assert.DoesNotContain("Kiriha", referencedNames);

        // Duplicate AniListAiringInfo DTO must be gone
        Assert.Null(typeof(AnimeEntity).Assembly.GetType("Kiriha.Core.Domain.Models.AniListAiringInfo"));

        // ScrobbleService must depend on IProgressUpdateService, not concrete AnimeProgressService
        var ctor = typeof(ScrobbleService).GetConstructors().Single();
        Assert.Equal(typeof(IProgressUpdateService), ctor.GetParameters()[0].ParameterType);
    }

    [Fact]
    public async Task LibraryMutationService_UsesDeterministicClockAndPublishesEventsViaCoreEventBus()
    {
        var fixedNow = new DateTimeOffset(2027, 3, 15, 18, 30, 0, TimeSpan.FromHours(2));
        var clock = new FakeTimeProvider(fixedNow);
        var bus = new CoreEventBus();

        var mockAnimeRepo = new Mock<IAnimeRepository>();
        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockSyncManager = new Mock<ISyncManager>();
        var mockHistory = new Mock<IHistoryService>();
        var mockUi = new Mock<IUiDispatcher>();
        mockUi.Setup(u => u.InvokeAsync(It.IsAny<Action>()))
            .Callback<Action>(a => a())
            .Returns(Task.CompletedTask);

        var mutations = new LibraryMutationService(
            mockAnimeRepo.Object,
            mockUserRepo.Object,
            mockSyncManager.Object,
            mockUi.Object,
            bus,
            clock);

        var progressService = new AnimeProgressService(
            mockAnimeRepo.Object,
            mockUserRepo.Object,
            mockSyncManager.Object,
            mockHistory.Object,
            mockUi.Object,
            mutations,
            bus,
            clock);

        var completedPrompts = new List<AnimeCompletedRatingPromptMessage>();
        var progressEvents = new List<AnimeProgressChangedMessage>();
        int refreshCount = 0;

        using var sub1 = bus.Subscribe<AnimeCompletedRatingPromptMessage>(completedPrompts.Add);
        using var sub2 = bus.Subscribe<AnimeProgressChangedMessage>(progressEvents.Add);
        using var sub3 = bus.Subscribe<AnimeListRefreshMessage>(_ => refreshCount++);

        var item = new AnimeEntity
        {
            Id = 52991,
            Title = "Sousou no Frieren",
            Status = UserAnimeStatus.PlanToWatch,
            Progress = 0,
            TotalEpisodes = 1
        };

        var nextStatus = await progressService.SmartIncrementProgressAsync(item, 1);

        Assert.Equal(UserAnimeStatus.Completed, nextStatus);
        Assert.Equal(new DateTime(2027, 3, 15), item.DateStarted);
        Assert.Equal(new DateTime(2027, 3, 15), item.DateCompleted);
        Assert.Single(completedPrompts);
        Assert.Single(progressEvents);
        Assert.Equal(1, refreshCount);

        // Unsubscribing stops delivery
        sub3.Dispose();
        bus.Publish(new AnimeListRefreshMessage());
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void UnifiedAnimeLibraryMatcher_And_SingleNormalizedIndex_WorkWithoutDuplicateDictionary()
    {
        // UserListIndex must no longer maintain a duplicate _normalized dictionary
        Assert.Null(typeof(MappingService.UserListIndex).GetField("_normalized", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.Null(typeof(MappingService.UserListIndex).GetMethod("FindNormalized", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

        var s1 = new AnimeEntity { Id = 1, MediaKind = MediaKind.Anime, Title = "Sousou no Frieren", RussianTitle = "Фрирен", TotalEpisodes = 28, StartYear = 2023 };
        var s2 = new AnimeEntity { Id = 2, MediaKind = MediaKind.Anime, Title = "Sousou no Frieren 2nd Season", RussianTitle = "Фрирен 2", TotalEpisodes = 24, StartYear = 2026 };

        // Active RSS matching resolves sequel and Russian titles accurately
        Assert.Same(s2, AnimeLibraryMatcher.MatchActiveAnime([s1, s2], "Sousou no Frieren", parsedSeason: 2));
        Assert.Same(s1, AnimeLibraryMatcher.MatchActiveAnime([s1, s2], "Фрирен", parsedSeason: null));

        // Release compatibility rejects Season 2 torrent against Season 1 selection, and accepts Season 2 selection
        const string s2Release = "[SubsPlease] Sousou no Frieren S02E03 (1080p).mkv";
        Assert.False(AnimeLibraryMatcher.IsReleaseCompatibleWithAnime(s1, s2Release));
        Assert.True(AnimeLibraryMatcher.IsReleaseCompatibleWithAnime(s2, s2Release));

        // Episode bound check rejects episode 30 for 28-ep Season 1 when query is non-exact
        Assert.False(AnimeLibraryMatcher.IsReleaseCompatibleWithAnime(
            s1,
            "[SubsPlease] Sousou no Frieren - 30 (1080p).mkv",
            activeSearchQuery: "Frieren"));
    }

    [Fact]
    public void AnimeEntity_CloneClearsSubscribers_ReactivePropertiesNotify_And_UserScoreNormalizes()
    {
        var original = new AnimeEntity
        {
            Id = 10,
            Title = "Monster",
            Score = "9 (Amazing)"
        };

        // Score string normalization & bidirectional UserScore
        Assert.Equal("9", original.Score);
        Assert.Equal(9, original.UserScore);

        original.UserScore = null;
        Assert.Equal("-", original.Score);
        Assert.Null(original.UserScore);

        original.UserScore = 10;
        Assert.Equal("10", original.Score);
        Assert.Equal(10, original.UserScore);

        // Clone() must NOT copy PropertyChanged subscribers from original
        int originalNotificationCount = 0;
        original.PropertyChanged += (_, _) => originalNotificationCount++;

        var clone = original.Clone();
        clone.Progress = 5;
        clone.Title = "Mutated Clone";
        clone.UserScore = 7;

        Assert.Equal(0, originalNotificationCount);

        // Reactive properties previously missing SetProperty must notify both property and Presentation
        var notifiedProps = new HashSet<string>(StringComparer.Ordinal);
        clone.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
                notifiedProps.Add(e.PropertyName);
        };

        clone.Chapters = 162;
        clone.ChaptersRead = 50;
        clone.Synopsis = "Updated synopsis";
        clone.RussianSynopsis = "Обновлённое описание";
        clone.StatusDetailed = "currently_airing";
        clone.LastEpisodeAt = DateTime.UtcNow;
        clone.NextEpisodeAt = DateTime.UtcNow.AddDays(3);

        Assert.Contains(nameof(AnimeEntity.Chapters), notifiedProps);
        Assert.Contains(nameof(AnimeEntity.ChaptersRead), notifiedProps);
        Assert.Contains(nameof(AnimeEntity.Synopsis), notifiedProps);
        Assert.Contains(nameof(AnimeEntity.RussianSynopsis), notifiedProps);
        Assert.Contains(nameof(AnimeEntity.StatusDetailed), notifiedProps);
        Assert.Contains(nameof(AnimeEntity.LastEpisodeAt), notifiedProps);
        Assert.Contains(nameof(AnimeEntity.NextEpisodeAt), notifiedProps);
        Assert.Contains(nameof(AnimeEntity.Presentation), notifiedProps);
    }

    [Fact]
    public async Task UserAnimeRepository_SyncFromRemote_PreservesHigherPriorityAiringFields()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var repo = new UserAnimeRepository(new SimpleDbFactory(options));
        var lastEpAt = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var nextEpAt = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

        // Initial local entry enriched by AniList/Shiki airing sync (priority 2, 9 episodes aired)
        await repo.SyncFromRemoteAsync(
        [
            new AnimeEntity
            {
                Id = 777,
                MediaKind = MediaKind.Anime,
                Title = "Ongoing Show",
                RussianTitle = "Онгоинг",
                Status = UserAnimeStatus.Watching,
                StatusDetailed = "currently_airing",
                Progress = 8,
                TotalEpisodes = 12,
                EpisodesAired = 9,
                AiredSourcePriority = 2,
                LastEpisodeAt = lastEpAt,
                NextEpisodeAt = nextEpAt,
                LocalPosterPath = "C:/cache/777.jpg"
            }
        ], [MediaKind.Anime]);

        // Remote MAL sync returns sparse airing fields (EpisodesAired = 0, AiredSourcePriority = 0)
        await repo.SyncFromRemoteAsync(
        [
            new AnimeEntity
            {
                Id = 777,
                MediaKind = MediaKind.Anime,
                Title = "Ongoing Show",
                Status = UserAnimeStatus.Watching,
                StatusDetailed = "currently_airing",
                Progress = 9,
                TotalEpisodes = 0,
                EpisodesAired = 0,
                AiredSourcePriority = 0
            }
        ], [MediaKind.Anime]);

        var persisted = await repo.GetByIdAsync(777, MediaKind.Anime);
        Assert.NotNull(persisted);
        Assert.Equal(9, persisted!.Progress);
        Assert.Equal(9, persisted.EpisodesAired);
        Assert.Equal(2, persisted.AiredSourcePriority);
        Assert.Equal(lastEpAt, persisted.LastEpisodeAt);
        Assert.Equal(nextEpAt, persisted.NextEpisodeAt);
        Assert.Equal(12, persisted.TotalEpisodes);
        Assert.Equal("Онгоинг", persisted.RussianTitle);
        Assert.Equal("C:/cache/777.jpg", persisted.LocalPosterPath);
    }

    private sealed class FakeTimeProvider(DateTimeOffset fixedLocalNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("TestTZ", fixedLocalNow.Offset, "TestTZ", "TestTZ");
        public override DateTimeOffset GetUtcNow() => fixedLocalNow.ToUniversalTime();
    }

    private sealed class SimpleDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
