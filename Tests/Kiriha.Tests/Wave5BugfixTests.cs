using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Kiriha.Core;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Navigation;
using Kiriha.Infrastructure.Platform;
using Kiriha.Localization;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Settings;
using Kiriha.Services.Notifications;
using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.Settings;
using Kiriha.Views.Converters;
using Kiriha.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiriha.Tests;

public class Wave5BugfixTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Kiriha.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate Kiriha.sln from test base directory.");
    }

    private sealed class NoOpFaviconService : FaviconService
    {
        public override Task<string?> TryGetFaviconAsync(string urlTemplate, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FormattingLocalizer : ILocalizer
    {
        private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["analytics.read_next.chapters_format"] = "+{0} гл.",
            ["analytics.read_next.read_progress"] = "Прочитано {0} из {1}",
            ["analytics.read_next.days_format"] = "{0} дн.",
            ["analytics.read_next.paused_on"] = "Пауза на {0} из {1}"
        };

        public string GetLoc(string key) =>
            _strings.TryGetValue(key, out var val) ? val : key;

        public string GetLoc(string key, params object?[] args) =>
            string.Format(CultureInfo.InvariantCulture, GetLoc(key), args);
    }

    // ─── 5.1 Custom Links Persistence (Clone Detachment Fix) ─────────────────

    [Fact]
    public void SettingsCustomLinksViewModel_MultipleEditsOnSameLink_PersistAcrossCloneAndReload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Kiriha_Wave5_Links_" + Guid.NewGuid());
        try
        {
            using (var settingsService = new SettingsService(tempDir))
            using (var faviconService = new NoOpFaviconService())
            {
                var vm = new SettingsCustomLinksViewModel(settingsService, faviconService);

                vm.AddCustomLinkCommand.Execute(null);
                Assert.Single(vm.CustomLinks);

                var link = vm.CustomLinks[0];

                // Simulate multiple sequential keystrokes/edits on the exact same UI instance.
                // Previously, the first SettingsService.Update cloned AppSettings.CustomLinks,
                // detaching the UI CustomShareLink reference so subsequent edits were lost.
                link.Name = "M";
                link.Name = "MyAnimeList";
                link.UrlTemplate = "https://myanimelist.net/";
                link.UrlTemplate = "https://myanimelist.net/anime/{malId}";
                link.IconPath = @"C:\Temp\mal.ico";

                Assert.Single(settingsService.Current.CustomLinks);
                Assert.Equal("MyAnimeList", settingsService.Current.CustomLinks[0].Name);
                Assert.Equal("https://myanimelist.net/anime/{malId}", settingsService.Current.CustomLinks[0].UrlTemplate);
                Assert.Equal(@"C:\Temp\mal.ico", settingsService.Current.CustomLinks[0].IconPath);
            }

            // Reload from disk to verify custom_links.json has the latest edits
            using (var reloaded = new SettingsService(tempDir))
            {
                Assert.Single(reloaded.Current.CustomLinks);
                Assert.Equal("MyAnimeList", reloaded.Current.CustomLinks[0].Name);
                Assert.Equal("https://myanimelist.net/anime/{malId}", reloaded.Current.CustomLinks[0].UrlTemplate);
                Assert.Equal(@"C:\Temp\mal.ico", reloaded.Current.CustomLinks[0].IconPath);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ─── 5.2 Analytics, Manga ReadNext, Localization & Heatmap ───────────────

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void AnalyticsLocalizationJson_HasZeroDuplicateKeysAndRequiredSections(string lang)
    {
        var repoRoot = FindRepoRoot();
        var jsonPath = Path.Combine(repoRoot, "src", "Kiriha", "Assets", "i18n", lang, "analytics.json");
        Assert.True(File.Exists(jsonPath), $"Missing {jsonPath}");

        var raw = File.ReadAllText(jsonPath, Encoding.UTF8);
        Assert.DoesNotContain("????", raw);

        using var doc = JsonDocument.Parse(raw);
        AssertNoDuplicateProperties(doc.RootElement, "$");

        Assert.True(doc.RootElement.TryGetProperty("tabs", out var tabs));
        Assert.True(tabs.TryGetProperty("tastes", out _));
        Assert.True(tabs.TryGetProperty("watch_next", out _));
        Assert.True(tabs.TryGetProperty("read_next", out _));
        Assert.True(tabs.TryGetProperty("history", out _));

        Assert.True(doc.RootElement.TryGetProperty("read_next", out var readNext));
        Assert.True(readNext.TryGetProperty("chapters_format", out _));
        Assert.True(readNext.TryGetProperty("read_progress", out _));
        Assert.True(readNext.TryGetProperty("days_format", out _));
        Assert.True(readNext.TryGetProperty("paused_on", out _));

        Assert.True(doc.RootElement.TryGetProperty("history", out var history));
        Assert.True(history.TryGetProperty("today", out _));
    }

    private static void AssertNoDuplicateProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in element.EnumerateObject())
            {
                Assert.True(seen.Add(prop.Name), $"Duplicate JSON property '{prop.Name}' at {path}");
                AssertNoDuplicateProperties(prop.Value, $"{path}.{prop.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (var item in element.EnumerateArray())
            {
                AssertNoDuplicateProperties(item, $"{path}[{idx++}]");
            }
        }
    }

    [Fact]
    public void ReadNextSectionViewModel_UsesMangaChaptersAndCleanLocalizedText()
    {
        var vm = new ReadNextSectionViewModel(new FormattingLocalizer());

        var ongoingManga = new AnimeEntity
        {
            Id = 1,
            MediaKind = MediaKind.Manga,
            Title = "Chainsaw Man",
            Status = UserAnimeStatus.Watching,
            StatusDetailed = "currently_publishing",
            ChaptersRead = 140,
            Chapters = 175,
            LocalPosterPath = @"C:\Posters\csm.jpg",
            MainPictureUrl = "https://cdn.example/csm.jpg"
        };

        var finishedManga = new AnimeEntity
        {
            Id = 2,
            MediaKind = MediaKind.Manga,
            Title = "Monster",
            Status = UserAnimeStatus.Watching,
            StatusDetailed = "finished",
            ChaptersRead = 62,
            Chapters = 162,
            DateStarted = DateTime.UtcNow.AddDays(-20),
            MainPictureUrl = "https://cdn.example/monster.jpg"
        };

        vm.Refresh([ongoingManga, finishedManga]);

        Assert.Single(vm.ReadTodo);
        Assert.Equal("+35 гл.", vm.ReadTodo[0].Badge);
        Assert.Equal("Прочитано 140 из 175", vm.ReadTodo[0].Detail);
        Assert.DoesNotContain("??", vm.ReadTodo[0].Badge);
        Assert.DoesNotContain("??", vm.ReadTodo[0].Detail);

        Assert.Single(vm.FinishedReadTodo);
        Assert.Equal("+100 гл.", vm.FinishedReadTodo[0].Badge);
        Assert.Equal("Прочитано 62 из 162", vm.FinishedReadTodo[0].Detail);

        Assert.Single(vm.StaleTodo);
        Assert.Contains("Пауза на 62 из 162", vm.StaleTodo[0].Detail);
        Assert.DoesNotContain("??", vm.StaleTodo[0].Badge);
    }

    [Fact]
    public void AnalyticsHelpers_EstimateHoursWatched_ExcludesMangaAndGetAccentHandlesAnyString()
    {
        var animeTv = new AnimeEntity { Id = 1, MediaKind = MediaKind.Anime, Type = "TV", Progress = 10 };
        var animeMovie = new AnimeEntity { Id = 2, MediaKind = MediaKind.Anime, Type = AppConstants.AnimeTypes.Movie, Progress = 2 };
        var manga = new AnimeEntity { Id = 3, MediaKind = MediaKind.Manga, Type = "Manga", Progress = 200, ChaptersRead = 200 };

        var hours = AnalyticsHelpers.EstimateHoursWatched([animeTv, animeMovie, manga]);
        var expected = (10 * 24 / 60.0) + (2 * 95 / 60.0);

        Assert.Equal(expected, hours, precision: 5);

        // Overflow safety check across diverse inputs
        Assert.StartsWith("#FF", AnalyticsHelpers.GetAccent(string.Empty));
        Assert.StartsWith("#FF", AnalyticsHelpers.GetAccent("Action"));
        Assert.StartsWith("#FF", AnalyticsHelpers.GetAccent("Slice of Life"));
    }

    [Fact]
    public void HistorySectionViewModel_IncludesTodayInRecentHistoryAndUsesReadableDarkHeatmapText()
    {
        var vm = new HistorySectionViewModel();
        var now = DateTime.Now;

        var anime = new AnimeEntity
        {
            Id = 42,
            MediaKind = MediaKind.Anime,
            Title = "Frieren",
            Status = UserAnimeStatus.Completed,
            DateCompleted = DateTime.UtcNow,
            Score = "10"
        };

        var todayHistory = new HistoryItem
        {
            AnimeId = 42,
            AnimeTitle = "Frieren",
            ActionType = 1,
            Episode = 28,
            Timestamp = now.ToUniversalTime()
        };

        vm.Refresh([todayHistory], [anime], [anime]);

        Assert.Equal(14, vm.RecentHistory.Count);
        var todayPoint = vm.RecentHistory[^1];
        Assert.Equal(0, todayPoint.DaysAgo);
        Assert.Equal(1, todayPoint.Count);
        Assert.Equal(1, vm.RecentHistoryEpisodes);

        vm.OpenDailyHistory(todayPoint);
        Assert.True(vm.IsHistoryPopupOpen);
        Assert.DoesNotContain("0 ", vm.HistoryPopupTitle); // Uses 'analytics.history.today', not '0 days ago'

        // Low-intensity cells (count == 0) in headless/dark theme should use light text (#FFE5E7EB), not dark #FF1F2937
        Assert.NotEmpty(vm.MonthlyHistory);
        var zeroCell = vm.MonthlyHistory[0].Months.First(m => m.Count == 0);
        Assert.Equal("#FFE5E7EB", zeroCell.TextColor);
    }

    [Fact]
    public void LocConverter_HandlesNullUnsetBooleanAndRussianThemesSafely()
    {
        var converter = new LocConverter();

        // Null / UnsetValue must never throw InvalidCastException or NullReferenceException
        Assert.Equal(true, converter.Convert(null, typeof(bool), "show_arrow", CultureInfo.InvariantCulture));
        Assert.Equal(true, converter.Convert(AvaloniaProperty.UnsetValue, typeof(bool), "show_arrow", CultureInfo.InvariantCulture));
        Assert.Equal(false, converter.Convert(true, typeof(bool), "show_arrow", CultureInfo.InvariantCulture));

        Assert.Equal("EyeOutline", converter.Convert(null, typeof(string), "eye_logic", CultureInfo.InvariantCulture));
        Assert.Equal("EyeOffOutline", converter.Convert(true, typeof(string), "eye_logic", CultureInfo.InvariantCulture));

        // Theme resolution via ThemeType, English names, and localization keys (no mojibake or hardcoded Cyrillic)
        Assert.Equal("Sun", converter.Convert(ThemeType.Light, typeof(string), "theme_icon", CultureInfo.InvariantCulture));
        Assert.Equal("Moon", converter.Convert(ThemeType.Dark, typeof(string), "theme_icon", CultureInfo.InvariantCulture));
        Assert.Equal("Monitor", converter.Convert(ThemeType.System, typeof(string), "theme_icon", CultureInfo.InvariantCulture));
        Assert.Equal("Sun", converter.Convert("light", typeof(string), "theme_icon", CultureInfo.InvariantCulture));
        Assert.Equal("Moon", converter.Convert("dark", typeof(string), "theme_icon", CultureInfo.InvariantCulture));
        Assert.Equal("Sun", converter.Convert("settings.theme.light", typeof(string), "theme_icon", CultureInfo.InvariantCulture));
        Assert.Equal("Moon", converter.Convert("settings.theme.dark", typeof(string), "theme_icon", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task LocalizationStore_Translate_IsSafeFromBackgroundThread()
    {
        var result = await Task.Run(() =>
        {
            var svc = new LocalizationService();
            var a = svc.GetLoc("analytics.tabs.history");
            var b = LocalizationStore.Translate("l.anime.types.tv");
            return (a, b);
        });

        Assert.NotNull(result.a);
        Assert.NotNull(result.b);
    }

    // ─── 5.3 Virtualization & XAML Polish ────────────────────────────────────

    [Fact]
    public void VirtualizedViews_UseItemsRepeaterAndScopedFhdBadge()
    {
        var repoRoot = FindRepoRoot();

        var torrentsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Kiriha", "Views", "Torrents", "TorrentsView.axaml"));
        Assert.Contains("ItemsRepeater ItemsSource=\"{Binding GroupedTorrents}\"", torrentsXaml);
        Assert.Contains("ItemsRepeater ItemsSource=\"{Binding OnlineResults}\"", torrentsXaml);
        Assert.Contains("ItemsRepeater ItemsSource=\"{Binding EpisodeFiles}\"", torrentsXaml);
        Assert.Contains("ItemsRepeater ItemsSource=\"{Binding OnlineEpisodes}\"", torrentsXaml);
        Assert.Contains("ItemsRepeater ItemsSource=\"{Binding DisplayedOnlineSources}\"", torrentsXaml);
        Assert.Contains("ShowActiveOnlineFhdBadge", torrentsXaml);

        var popupXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Kiriha", "Views", "Analytics", "AnalyticsHistoryPopup.axaml"));
        Assert.Contains("ItemsRepeater ItemsSource=\"{Binding History.HistoryPopupEntries}\"", popupXaml);

        var templatesXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Kiriha", "Views", "Torrents", "TorrentTemplates.axaml"));
        Assert.Contains("Anime.PosterSource", templatesXaml);

        var nowPlayingHeaderXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Kiriha", "Views", "Controls", "Variants", "NowPlayingCompactSections", "NowPlayingCompactHeader.axaml"));
        Assert.Contains("MatchedAnime.Presentation.DisplayTitle", nowPlayingHeaderXaml);
        Assert.Contains("MatchedAnime.PosterSource", nowPlayingHeaderXaml);
    }

    [Fact]
    public void MalAndAniListMappers_FormatMeanScoreWithInvariantDotUnderRussianCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            using var malDoc = JsonDocument.Parse("""{"id":52991,"title":"Sousou no Frieren","mean":8.45,"media_type":"tv"}""");
            var malEntity = Kiriha.Core.Tracking.Api.MalMapper.MapJsonToAnimeEntity(malDoc.RootElement);
            Assert.Equal("8.45", malEntity.MeanScore);

            using var aniDoc = JsonDocument.Parse("""{"id":154587,"idMal":52991,"averageScore":84.5,"format":"TV","title":{"romaji":"Sousou no Frieren"}}""");
            var aniEntity = Kiriha.Core.Tracking.Api.AniListMapper.MapMediaToAnimeEntity(aniDoc.RootElement);
            Assert.Equal("8.45", aniEntity.MeanScore);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // ─── 5.4 Transient IDisposable Leak, ImageDownloader & Font Dedup ────────

    public sealed class DisposableTransientTestVm : ViewModelBase, IDisposable
    {
        public void Dispose() { }
    }

    public sealed class SingletonTestVm : ViewModelBase;

    [Fact]
    public void ViewModelFactory_DoesNotRetainTransientDisposableViewModelsInRootProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SingletonTestVm>();
        services.AddTransient<DisposableTransientTestVm>();

        var transientTypes = new HashSet<Type> { typeof(DisposableTransientTestVm) };
        using var provider = services.BuildServiceProvider();
        var factory = new ViewModelFactory(provider, transientTypes);

        // Singletons still return the exact same instance
        var s1 = factory.Create<SingletonTestVm>();
        var s2 = factory.Create<SingletonTestVm>();
        Assert.Same(s1, s2);

        // Transient IDisposable created inside a non-inlined method must be collectable
        // while the root ServiceProvider is still alive.
        var weakRef = CreateTransientWeakRef(factory);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weakRef.IsAlive, "Root ServiceProvider leaked a strong reference to transient IDisposable ViewModel.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateTransientWeakRef(IViewModelFactory factory)
    {
        var instance = factory.Create<DisposableTransientTestVm>();
        return new WeakReference(instance);
    }

    private sealed class DelayedHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly byte[] _payload = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task ImageDownloader_CancellingFirstCallerDoesNotAbortSecondCallerSharingSameUrl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Kiriha_Wave5_Img_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            using var handler = new DelayedHttpHandler();
            using var downloader = new ImageDownloader(new StubHttpClientFactory(handler), tempDir);

            using var ctsA = new CancellationTokenSource();
            using var ctsB = new CancellationTokenSource();

            const string url = "https://cdn.example/posters/12345.png";
            var taskA = downloader.GetLocalPathOrDownload(url, ctsA.Token);
            var taskB = downloader.GetLocalPathOrDownload(url, ctsB.Token);

            // Cancel caller A while download is in-flight
            ctsA.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => taskA);

            // Release HTTP response; caller B should still receive the downloaded local file path!
            handler.Release();
            var pathB = await taskB;

            Assert.False(string.IsNullOrEmpty(pathB));
            Assert.True(File.Exists(pathB));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void KirihaCsproj_ExcludesDuplicateFontsFromAvaloniaResource()
    {
        var csprojPath = Path.Combine(FindRepoRoot(), "src", "Kiriha", "Kiriha.csproj");
        var csproj = File.ReadAllText(csprojPath);
        Assert.Contains("<AvaloniaResource Remove=\"Assets\\Fonts\\**\" />", csproj);
    }

    // ─── 5.5 Keyboard, Security, Search Badge & Toasts ───────────────────────

    [Fact]
    public void MainWindowXaml_DoesNotBindReleaseF9CrashShortcut()
    {
        var xamlPath = Path.Combine(FindRepoRoot(), "src", "Kiriha", "Views", "MainWindow.axaml");
        var xaml = File.ReadAllText(xamlPath);
        Assert.DoesNotContain("Gesture=\"F9\"", xaml);
    }

    [Fact]
    public void AnimeEntityPresentation_HasTotalEpisodesAndHasMeanScore_GuardZeroAndPlaceholders()
    {
        var unknown = new AnimeEntity { Id = 1, TotalEpisodes = 0, MeanScore = "-" };
        Assert.False(unknown.Presentation.HasTotalEpisodes);
        Assert.False(unknown.Presentation.HasMeanScore);

        var known = new AnimeEntity { Id = 2, TotalEpisodes = 12, MeanScore = "8.45" };
        Assert.True(known.Presentation.HasTotalEpisodes);
        Assert.True(known.Presentation.HasMeanScore);
    }

    [Theory]
    [InlineData("https://myanimelist.net/anime/52991", true)]
    [InlineData("http://localhost:8090/stream", true)]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData(@"C:\Windows\System32\cmd.exe", false)]
    [InlineData(@"\\evil.server\share\payload.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShellLauncher_IsAllowedExternalUrl_EnforcesSafeSchemes(string? url, bool expected)
    {
        Assert.Equal(expected, ShellLauncher.IsAllowedExternalUrl(url));
    }

    [Theory]
    [InlineData("https://animelib.org/ru", true)]
    [InlineData("https://v2.animelib.org/api/me", true)]
    [InlineData("https://api.cdnlibs.org/api/anime", true)]
    [InlineData("https://anilib.me/ru/anime", true)]
    [InlineData("https://oauth.vk.com/authorize", false)]
    [InlineData("https://accounts.google.com/signin", false)]
    [InlineData("https://shikimori.one/oauth", false)]
    [InlineData("https://evil-animelib.org/login", false)]
    [InlineData(null, false)]
    public void AnimeLibLoginWindow_IsAnimeLibAuthHost_WhitelistsOfficialDomainsOnly(string? uri, bool expected)
    {
        Assert.Equal(expected, AnimeLibLoginWindow.IsAnimeLibAuthHost(uri));
    }

    [Fact]
    public void AnimeLibLoginWindow_ParseScriptStringResult_UnescapesJsonProperly()
    {
        Assert.Null(AnimeLibLoginWindow.ParseScriptStringResult(null));
        Assert.Null(AnimeLibLoginWindow.ParseScriptStringResult("null"));
        Assert.Null(AnimeLibLoginWindow.ParseScriptStringResult("\"\""));
        Assert.Equal("eyJhbGciOiJIUzI1NiJ9.payload.sig",
            AnimeLibLoginWindow.ParseScriptStringResult("\"eyJhbGciOiJIUzI1NiJ9.payload.sig\""));
        Assert.Equal("token\"with\\escapes",
            AnimeLibLoginWindow.ParseScriptStringResult("\"token\\\"with\\\\escapes\""));
    }

    [Fact]
    public void ToastRenderer_BuildToastXml_ClampsToThreeLinesAndResolvesLocalImagesOnly()
    {
        var tempImg = Path.Combine(Path.GetTempPath(), $"kiriha_toast_{Guid.NewGuid()}.png");
        File.WriteAllBytes(tempImg, [1, 2, 3, 4]);
        try
        {
            // 4 lines passed -> clamped to 3 <text> elements so ToastGeneric XML never rejects it
            var xmlWithLocal = ToastRenderer.BuildToastXml(
                ["Kiriha <Update>", "Sousou no Frieren & More", "Episode 12", "Dropped 4th line"],
                tempImg);

            Assert.Equal(3, CountOccurrences(xmlWithLocal, "<text>"));
            Assert.Contains("Kiriha &lt;Update&gt;", xmlWithLocal);
            Assert.Contains("Sousou no Frieren &amp; More", xmlWithLocal);
            Assert.DoesNotContain("Dropped 4th line", xmlWithLocal);
            Assert.Contains("<image placement=\"appLogoOverride\" hint-crop=\"circle\" src=\"file:///", xmlWithLocal);

            // Remote http/https URLs are unsupported by unpackaged Win32 ToastGeneric -> omitted cleanly
            var xmlWithRemote = ToastRenderer.BuildToastXml(
                ["Title", "Body"],
                "https://cdn.myanimelist.net/images/anime/1015/138006.jpg");
            Assert.DoesNotContain("<image", xmlWithRemote);
        }
        finally
        {
            if (File.Exists(tempImg)) File.Delete(tempImg);
        }
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
