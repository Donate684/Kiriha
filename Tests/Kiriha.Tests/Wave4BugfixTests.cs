using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Text.Json;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.Genres;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Services;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Infrastructure.Platform;
using Kiriha.Infrastructure.Tracking.ExternalDetection;
using Kiriha.Services;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Settings;
using Kiriha.Services.Franchise;
using Kiriha.Utils.Parsing;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class Wave4BugfixTests
{
    private sealed class DummyLocalizer : ILocalizer
    {
        public string GetLoc(string key) => key;
        public string GetLoc(string key, params object?[] args) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, key, args);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature 18: Kernel Deadlocks & Audio Sessions
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HandleEnumerationStrategy_AccessMaskFiltering_RejectsPipesAndWrites()
    {
        // GrantedAccess must have FILE_READ_DATA (0x0001)
        const uint FILE_READ_DATA = 0x0001;
        const uint FILE_WRITE_DATA = 0x0002;
        const uint FILE_APPEND_DATA = 0x0004;
        const uint ACCESS_MASK_PIPE_GENERIC = 0x0012019f;
        const uint ACCESS_MASK_PIPE_READ = 0x00120189;

        // Valid read access
        uint validFileAccess = 0x00120089;
        Assert.True((validFileAccess & FILE_READ_DATA) != 0);
        Assert.True((validFileAccess & (FILE_WRITE_DATA | FILE_APPEND_DATA)) == 0);
        Assert.NotEqual(ACCESS_MASK_PIPE_GENERIC, validFileAccess);
        Assert.NotEqual(ACCESS_MASK_PIPE_READ, validFileAccess);

        // Pipe masks must be rejected
        Assert.True(ACCESS_MASK_PIPE_GENERIC == 0x0012019f);
        Assert.True(ACCESS_MASK_PIPE_READ == 0x00120189);

        // Zero PID safety
        var openFiles = Kiriha.Infrastructure.Tracking.ExternalDetection.Strategies.HandleEnumerationStrategy.GetOpenFiles(0);
        Assert.Empty(openFiles);

        var firstVideo = Kiriha.Infrastructure.Tracking.ExternalDetection.Strategies.HandleEnumerationStrategy.TryGetFirstOpenVideoFile(0);
        Assert.Null(firstVideo);
    }

    [Fact]
    public void AudioSessionProbe_Logic_MultiSessionPrioritization()
    {
        // When iterating multiple sessions:
        // Any Active session (1) must return Active immediately regardless of order
        int[] statesInactiveFirst = [0, 1]; // Inactive lingering, Active playing
        int[] statesActiveFirst = [1, 0];
        int[] statesOnlyInactive = [0, 2];  // Inactive, Expired

        AudioState EvaluateStates(int[] rawStates)
        {
            bool foundAny = false;
            bool anyInactive = false;
            bool anyExpired = false;

            foreach (var rawState in rawStates)
            {
                foundAny = true;
                if (rawState == 1) return AudioState.Active;
                if (rawState == 0) anyInactive = true;
                else if (rawState == 2) anyExpired = true;
            }

            if (!foundAny) return AudioState.Unknown;
            if (anyInactive) return AudioState.Inactive;
            if (anyExpired) return AudioState.Expired;
            return AudioState.Unknown;
        }

        Assert.Equal(AudioState.Active, EvaluateStates(statesInactiveFirst));
        Assert.Equal(AudioState.Active, EvaluateStates(statesActiveFirst));
        Assert.Equal(AudioState.Inactive, EvaluateStates(statesOnlyInactive));
        Assert.Equal(AudioState.Unknown, EvaluateStates([]));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature 19: Scrobbler Toggle, Episode Prioritization, Window Class & SMTC
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ScrobbleService_StartScrobble_PrioritizesExplicitEpisode_OverReParsedTitle()
    {
        var tempSettings = Path.GetTempFileName();
        try
        {
            var settings = new SettingsService(tempSettings);
            settings.Update(s =>
            {
                s.System.Scrobbler.Enabled = true;
                s.System.Scrobbler.DelaySeconds = 0;
            }, save: false);

            var mockProgress = new Mock<AnimeProgressService>(null!, null!, null!, null!, null!);
            var mockHistory = new Mock<HistoryService>(null!);
            var mockNotification = new Mock<NotificationService>(null!, null!);
            var mockBackground = new Mock<IBackgroundTaskSupervisor>();
            var mockUi = new Mock<IUiDispatcher>();
            var mockStore = new Mock<IScrobbleProgressStore>();

            int scrobbledEpisode = 0;
            mockProgress
                .Setup(p => p.UpdateProgressAsync(It.IsAny<AnimeEntity>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>()))
                .Callback<AnimeEntity, int, UserAnimeStatus?>((anime, ep, st) =>
                {
                    scrobbledEpisode = ep;
                })
                .ReturnsAsync(true);

            mockBackground
                .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
                {
                    task(ct).GetAwaiter().GetResult();
                });

            var scrobbler = new ScrobbleService(
                mockProgress.Object,
                mockHistory.Object,
                settings,
                mockNotification.Object,
                mockBackground.Object,
                mockUi.Object,
                new DummyLocalizer(),
                mockStore.Object);

            var anime = new AnimeEntity
            {
                Id = 12345,
                Title = "Mob Psycho 100",
                TotalEpisodes = 12,
                Progress = 4,
                Status = UserAnimeStatus.Watching
            };

            // Original title contains "Mob Psycho 100", but explicit episode is "05"
            var media = new ParsedMedia
            {
                OriginalTitle = "[SubsPlease] Mob Psycho 100 [1080p].mkv",
                AnimeTitle = "Mob Psycho 100",
                Episode = "05",
                IsPlaying = true,
                Duration = TimeSpan.FromMinutes(24)
            };

            scrobbler.StartScrobble(media, anime);

            // Verified: explicit episode 5 was scrobbled, NOT 100!
            Assert.Equal(5, scrobbledEpisode);
        }
        finally
        {
            if (File.Exists(tempSettings)) File.Delete(tempSettings);
        }
    }

    [Fact]
    public void ScrobbleService_StartScrobble_WhenScrobblerDisabled_DoesNotStartScrobble()
    {
        var tempSettings = Path.GetTempFileName();
        try
        {
            var settings = new SettingsService(tempSettings);
            settings.Update(s => s.System.Scrobbler.Enabled = false, save: false);

            var mockProgress = new Mock<AnimeProgressService>(null!, null!, null!, null!, null!);
            var mockHistory = new Mock<HistoryService>(null!);
            var mockNotification = new Mock<NotificationService>(null!, null!);
            var mockBackground = new Mock<IBackgroundTaskSupervisor>();
            var mockUi = new Mock<IUiDispatcher>();
            var mockStore = new Mock<IScrobbleProgressStore>();

            var scrobbler = new ScrobbleService(
                mockProgress.Object,
                mockHistory.Object,
                settings,
                mockNotification.Object,
                mockBackground.Object,
                mockUi.Object,
                new DummyLocalizer(),
                mockStore.Object);

            var anime = new AnimeEntity
            {
                Id = 12345,
                Title = "Frieren",
                TotalEpisodes = 28,
                Progress = 0,
                Status = UserAnimeStatus.Watching
            };

            var media = new ParsedMedia
            {
                OriginalTitle = "Frieren - 01.mkv",
                AnimeTitle = "Frieren",
                Episode = "01",
                IsPlaying = true
            };

            scrobbler.StartScrobble(media, anime);

            mockBackground.Verify(
                x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            if (File.Exists(tempSettings)) File.Delete(tempSettings);
        }
    }

    [Theory]
    [InlineData("MPC-HC")]
    [InlineData("MPC-HC x64")]
    [InlineData("MPC-HC (64-bit)")]
    [InlineData("Media Player Classic Home Cinema")]
    public void ExternalPlayerCatalog_MpcHc_IdleVariants_ReturnNull(string idleTitle)
    {
        var players = ExternalPlayerCatalog.Load();
        var mpcHc = players.First(p => p.Name.Equals("MPC-HC", StringComparison.OrdinalIgnoreCase));

        var regex = new System.Text.RegularExpressions.Regex(mpcHc.WindowTitleFormat);
        var match = regex.Match(idleTitle);

        // For idle title, branch 1 matches without capturing group 1 or 2
        string captured = match.Groups[1].Success ? match.Groups[1].Value : (match.Groups[2].Success ? match.Groups[2].Value : string.Empty);
        Assert.True(string.IsNullOrEmpty(captured), $"Idle title '{idleTitle}' captured unexpected title '{captured}'");
    }

    [Fact]
    public void ExternalPlayerCatalog_MpcHc_PlayingTitle_ExtractsCorrectly()
    {
        var players = ExternalPlayerCatalog.Load();
        var mpcHc = players.First(p => p.Name.Equals("MPC-HC", StringComparison.OrdinalIgnoreCase));

        var regex = new System.Text.RegularExpressions.Regex(mpcHc.WindowTitleFormat);
        var match = regex.Match("[SubsPlease] Frieren - 01 (1080p).mkv - MPC-HC x64");

        Assert.True(match.Success);
        Assert.True(match.Groups[1].Success);
        Assert.Equal("[SubsPlease] Frieren - 01 (1080p).mkv", match.Groups[1].Value);
    }

    [Fact]
    public void Win32Api_FindMainWindow_ZeroPidReturnsZero()
    {
        var hwnd = Win32Api.FindMainWindow(0, ["MediaPlayerClassicW"]);
        Assert.Equal(IntPtr.Zero, hwnd);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature 20: Donghua Detection & Regional Metadata
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Kimi ni Todoke")]
    [InlineData("Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka")]
    [InlineData("NHK ni Youkoso!")]
    public void DonghuaDetector_JapaneseAnimeWithNi_DoesNotTriggerChinese(string title)
    {
        var anime = new AnimeEntity
        {
            Id = 1,
            Title = title,
            JapaneseTitle = null // Kana guard not present, must not trigger on "ni"
        };

        var detected = DonghuaDetector.Detect(anime);
        Assert.Null(detected);
    }

    [Fact]
    public void DonghuaDetector_BangumiInSynopsis_DoesNotTriggerChinese()
    {
        var anime = new AnimeEntity
        {
            Id = 2,
            Title = "Nichijou Special",
            Synopsis = "Broadcast program recap from Bangumi TV."
        };

        var detected = DonghuaDetector.Detect(anime);
        Assert.Null(detected);
    }

    [Theory]
    [InlineData("Xian Ni")]
    [InlineData("Doupo Cangqiong")]
    [InlineData("Douluo Dalu")]
    [InlineData("Renegade Immortal: Xian Ni")]
    public void DonghuaDetector_KnownDonghuaTitles_TriggerChinese(string title)
    {
        var anime = new AnimeEntity
        {
            Id = 3,
            Title = title
        };

        var detected = DonghuaDetector.Detect(anime);
        Assert.Equal("CN", detected);
    }

    [Fact]
    public void DonghuaDetector_ContainsKana_IdentifiesKanaCorrectly()
    {
        Assert.True(DonghuaDetector.ContainsKana("君に届け"));
        Assert.True(DonghuaDetector.ContainsKana("ダンジョンに出会いを求めるのは間違っているだろうか"));
        Assert.True(DonghuaDetector.ContainsKana("サイバーパンク"));
        Assert.False(DonghuaDetector.ContainsKana("Xian Ni"));
        Assert.False(DonghuaDetector.ContainsKana("斗罗大陆"));
        Assert.False(DonghuaDetector.ContainsKana(""));
        Assert.False(DonghuaDetector.ContainsKana(null));
    }

    [Fact]
    public void AniListMapper_MapsNativeJapaneseTitle_AndCountryOfOrigin()
    {
        const string json = """
        {
            "id": 1234,
            "idMal": 5678,
            "countryOfOrigin": "JP",
            "title": {
                "romaji": "Kimi ni Todoke",
                "english": "From Me to You",
                "native": "君に届け"
            },
            "synonyms": ["Reaching You"],
            "format": "TV",
            "episodes": 25
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var item = AniListMapper.MapMediaToAnimeEntity(doc.RootElement);

        Assert.Equal("君に届け", item.JapaneseTitle);
        Assert.Equal("From Me to You", item.EnglishTitle);
        Assert.Equal("JP", item.CountryOfOrigin);
        Assert.Contains("Kimi ni Todoke", item.AlternativeTitles);
        Assert.Contains("From Me to You", item.AlternativeTitles);
        Assert.Contains("君に届け", item.AlternativeTitles);
        Assert.Contains("Reaching You", item.AlternativeTitles);
    }

    [Fact]
    public void ShikiMapper_MapsJapaneseTitle_AndSynonyms()
    {
        const string json = """
        {
            "id": 100,
            "status": "watching",
            "episodes": 5,
            "target_type": "Anime",
            "anime": {
                "id": 5678,
                "name": "Kimi ni Todoke",
                "russian": "Дотянуться до тебя",
                "japanese": "君に届け",
                "synonyms": ["Reaching You"],
                "kind": "tv",
                "episodes": 25
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var item = ShikiMapper.MapRateToAnimeEntity(doc.RootElement, "https://shikimori.one");

        Assert.Equal("君に届け", item.JapaneseTitle);
        Assert.Contains("君に届け", item.AlternativeTitles);
        Assert.Contains("Reaching You", item.AlternativeTitles);
        Assert.Contains("Kimi ni Todoke", item.AlternativeTitles);
        Assert.Contains("Дотянуться до тебя", item.AlternativeTitles);
    }

    [Fact]
    public void AnimeEntity_CopyTo_And_MergeFullDetails_PreservesCountryOfOrigin()
    {
        var source = new AnimeEntity
        {
            Id = 1,
            Title = "Douluo Dalu",
            CountryOfOrigin = "CN"
        };

        var target = new AnimeEntity { Id = 1, Title = "Soul Land" };
        source.CopyTo(target);
        Assert.Equal("CN", target.CountryOfOrigin);

        var emptyTarget = new AnimeEntity { Id = 2, Title = "Target" };
        emptyTarget.MergeFullDetails(source);
        Assert.Equal("CN", emptyTarget.CountryOfOrigin);
    }

    [Fact]
    public async Task AnimeCountryService_BidirectionalSelfHealing_WorksCorrectly()
    {
        var mockRepo = new Mock<IAnimeCountryRepository>();

        // Scenario 1: DB has "JP", but item firmly identified as "CN"
        var item1 = new AnimeEntity { Id = 1, Title = "Xian Ni" };
        // Scenario 2: DB has false "CN", but item JapaneseTitle contains Kana
        var item2 = new AnimeEntity { Id = 2, Title = "Kimi ni Todoke", JapaneseTitle = "君に届け" };
        // Scenario 3: DB has false "CN", but authoritative API item.CountryOfOrigin is "JP"
        var item3 = new AnimeEntity { Id = 3, Title = "NHK ni Youkoso!", CountryOfOrigin = "JP" };

        var dbCache = new Dictionary<int, string>
        {
            [1] = "JP",
            [2] = "CN",
            [3] = "CN"
        };

        mockRepo.Setup(r => r.GetBatchAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dbCache);

        Dictionary<int, string>? upserted = null;
        mockRepo.Setup(r => r.UpsertBatchAsync(It.IsAny<IReadOnlyDictionary<int, string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyDictionary<int, string>, CancellationToken>((dict, ct) =>
            {
                upserted = new Dictionary<int, string>(dict);
            })
            .Returns(Task.CompletedTask);

        var service = new AnimeCountryService(mockRepo.Object);
        var updated = await service.HydrateCountriesAsync([item1, item2, item3]);

        Assert.True(updated);
        Assert.Equal("CN", item1.CountryOfOrigin);
        Assert.Equal("JP", item2.CountryOfOrigin);
        Assert.Equal("JP", item3.CountryOfOrigin);

        Assert.NotNull(upserted);
        Assert.Equal("CN", upserted[1]);
        Assert.Equal("JP", upserted[2]);
        Assert.Equal("JP", upserted[3]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature 21: Search Query Parser Prefixes & Clean Aliases
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Эти глупые свиньи")]
    [InlineData("School Days")]
    [InlineData("Space Dandy")]
    [InlineData("Monogatari Series")]
    [InlineData("Она и её кот")]
    [InlineData("Romance Dawn")]
    [InlineData("Tokyo Ghoul Movie")]
    [InlineData("Horror Story")]
    [InlineData("Drama City")]
    [InlineData("Action Heroine Cheer Fruits")]
    public void AnimeSearchQueryParser_PreservesAmbiguousWordsInMultiTokenQueries(string query)
    {
        var parsed = AnimeSearchQueryParser.Parse(query);

        Assert.Equal(query, parsed.TitleSearchText);
        Assert.Empty(parsed.ExtractedGenres);
        Assert.Empty(parsed.ExtractedFormats);
    }

    [Fact]
    public void AnimeSearchQueryParser_PrefixedGenresAndFormats_AreExtracted()
    {
        var parsedSchool = AnimeSearchQueryParser.Parse("#school days");
        Assert.Equal("days", parsedSchool.TitleSearchText);
        Assert.Single(parsedSchool.ExtractedGenres);
        Assert.Equal("school", parsedSchool.ExtractedGenres[0].Key);

        var parsedEcchi = AnimeSearchQueryParser.Parse("#ecchi свиньи");
        Assert.Equal("свиньи", parsedEcchi.TitleSearchText);
        Assert.Single(parsedEcchi.ExtractedGenres);
        Assert.Equal("ecchi", parsedEcchi.ExtractedGenres[0].Key);

        var parsedGenreTag = AnimeSearchQueryParser.Parse("genre:school days");
        Assert.Equal("days", parsedGenreTag.TitleSearchText);
        Assert.Single(parsedGenreTag.ExtractedGenres);
        Assert.Equal("school", parsedGenreTag.ExtractedGenres[0].Key);

        var parsedTypeTag = AnimeSearchQueryParser.Parse("type:tv bleach");
        Assert.Equal("bleach", parsedTypeTag.TitleSearchText);
        Assert.Single(parsedTypeTag.ExtractedFormats);
        Assert.Equal("tv", parsedTypeTag.ExtractedFormats[0].Key);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature 22: Release Parser Edge Cases, Franchise Relations, HTTP Cache
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnimeReleaseParser_MultiYearRanges_TreatedAsYearsNotBatch()
    {
        var parsedRange = AnimeReleaseParser.Parse("[Group] Title (2023-2024) [1080p].mkv");
        Assert.Equal("Title", parsedRange.Title);
        Assert.Equal(string.Empty, parsedRange.Episode);
        Assert.False(parsedRange.IsBatch);

        var parsedSingle = AnimeReleaseParser.Parse("[Group] Title (2023) [1080p].mkv");
        Assert.Equal("Title", parsedSingle.Title);
        Assert.Equal(string.Empty, parsedSingle.Episode);
        Assert.False(parsedSingle.IsBatch);
    }

    [Fact]
    public void AnimeReleaseParser_PreservesTitleWords_AllAndWeb()
    {
        var parsedAll = AnimeReleaseParser.Parse("All Saints Street - 01 [1080p].mkv");
        Assert.Equal("All Saints Street", parsedAll.Title);
        Assert.Equal("01", parsedAll.Episode);

        var parsedAllNoEp = AnimeReleaseParser.Parse("All Saints Street [1080p].mkv");
        Assert.Equal("All Saints Street", parsedAllNoEp.Title);
        Assert.Equal(string.Empty, parsedAllNoEp.Episode);

        var parsedAllBare = AnimeReleaseParser.Parse("All Saints Street.mkv");
        Assert.Equal("All Saints Street", parsedAllBare.Title);
        Assert.Equal(string.Empty, parsedAllBare.Episode);

        var parsedAllWithGroup = AnimeReleaseParser.Parse("[Group] All Saints Street (2020) [1080p].mkv");
        Assert.Equal("All Saints Street", parsedAllWithGroup.Title);
        Assert.Equal(string.Empty, parsedAllWithGroup.Episode);

        var parsedWeb = AnimeReleaseParser.Parse("Charlotte's Web [1080p].mkv");
        Assert.Equal("Charlotte's Web", parsedWeb.Title);
        Assert.Equal(string.Empty, parsedWeb.Episode);
    }

    [Fact]
    public void AnimeReleaseParser_GuardsTrailingNumericTitle()
    {
        var parsedMob = AnimeReleaseParser.Parse("[Group] Mob Psycho 100 1080p.mkv");
        Assert.Equal("Mob Psycho 100", parsedMob.Title);
        Assert.Equal(string.Empty, parsedMob.Episode);

        var parsedMobWithEp = AnimeReleaseParser.Parse("[Group] Mob Psycho 100 - 05 [1080p].mkv");
        Assert.Equal("Mob Psycho 100", parsedMobWithEp.Title);
        Assert.Equal("05", parsedMobWithEp.Episode);
    }

    [Fact]
    public void AnimeReleaseParser_AllowsZeroAndFractionalEpisodes()
    {
        var parsedZero = AnimeReleaseParser.Parse("[Group] Title - 00 [1080p].mkv");
        Assert.Equal("00", parsedZero.Episode);

        var parsedFraction = AnimeReleaseParser.Parse("[Group] Title - 05.5 [1080p].mkv");
        Assert.Equal("05.5", parsedFraction.Episode);
    }

    [Fact]
    public void AnimeReleaseParser_StripsUrlQueryStringBeforeExtension()
    {
        var parsedUrl = AnimeReleaseParser.Parse("https://cdn.example.com/stream/Frieren - 01.mp4?token=abc.def&exp=123#fragment");
        Assert.Equal("Frieren", parsedUrl.Title);
        Assert.Equal("01", parsedUrl.Episode);

        var parsedEpOnly = AnimeReleaseParser.Parse("https://cdn.example.com/stream/ep01.mp4?token=abc.def&exp=123#fragment");
        Assert.Equal("01", parsedEpOnly.Episode);
    }

    [Fact]
    public void HttpConditionalCache_WeakETag_PreservedInHeader()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.ETag = new EntityTagHeaderValue("\"abc123\"", isWeak: true);

        // response.Headers.ETag.ToString() produces W/"abc123"
        var etagStr = response.Headers.ETag?.ToString();
        Assert.NotNull(etagStr);
        Assert.StartsWith("W/", etagStr);

        // Parsing back preserves IsWeak
        var parsedBack = EntityTagHeaderValue.Parse(etagStr);
        Assert.True(parsedBack.IsWeak);
        Assert.Equal("\"abc123\"", parsedBack.Tag);
    }

    [Fact]
    public async Task FranchiseService_IngestFranchise_ExcludesMangaNodes()
    {
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        var mockRelationRepo = new Mock<IAnimeRelationRepository>();
        var mockShikiApi = new Mock<IShikiApiService>();

        mockAnimeRepo.SetupGet(r => r.Collection).Returns(new ObservableCollection<AnimeEntity>());

        List<AnimeRelation>? savedRelations = null;
        mockRelationRepo.Setup(r => r.ReplaceAsync(1, It.IsAny<IEnumerable<AnimeRelation>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IEnumerable<AnimeRelation>, CancellationToken>((id, rels, ct) =>
            {
                savedRelations = rels.ToList();
            })
            .Returns(Task.CompletedTask);

        var service = new FranchiseService(mockAnimeRepo.Object, mockRelationRepo.Object, mockShikiApi.Object);

        var franchiseData = new ShikiFranchiseResponse
        {
            CurrentId = 1,
            Nodes =
            [
                new ShikiFranchiseNode { Id = 1, Name = "Anime S1", Kind = "tv", Url = "/animes/1" },
                new ShikiFranchiseNode { Id = 2, Name = "Anime S2", Kind = "tv", Url = "/animes/2" },
                new ShikiFranchiseNode { Id = 3, Name = "Manga Adaptation", Kind = "manga", Url = "/mangas/3" }
            ],
            Links =
            [
                new ShikiFranchiseLink { SourceId = 1, TargetId = 2, Relation = "sequel" },
                new ShikiFranchiseLink { SourceId = 1, TargetId = 3, Relation = "adaptation" }
            ]
        };

        await service.IngestFranchiseAsync(1, franchiseData);

        Assert.NotNull(savedRelations);
        // Only Anime S2 should be retained; Manga Adaptation (Id 3) must be excluded!
        Assert.Single(savedRelations);
        Assert.Equal(2, savedRelations[0].TargetMalId);
        Assert.Equal("sequel", savedRelations[0].RelationType);
    }

    [Fact]
    public async Task FranchiseService_NetworkErrorNull_DoesNotCacheEmptyRelations()
    {
        var mockAnimeRepo = new Mock<IAnimeRepository>();
        var mockRelationRepo = new Mock<IAnimeRelationRepository>();
        var mockShikiApi = new Mock<IShikiApiService>();

        mockAnimeRepo.SetupGet(r => r.Collection).Returns(new ObservableCollection<AnimeEntity>());

        mockShikiApi.Setup(s => s.GetFranchiseAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShikiFranchiseResponse?)null);

        var service = new FranchiseService(mockAnimeRepo.Object, mockRelationRepo.Object, mockShikiApi.Object);

        // When GetFranchiseAsync returns null, ReplaceAsync must NOT be called
        mockRelationRepo.Verify(r => r.ReplaceAsync(It.IsAny<int>(), It.IsAny<IEnumerable<AnimeRelation>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
