using System.Diagnostics;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Utils.Sorting;
using Kiriha.Mpv;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Services.Data.Settings;
using Kiriha.Utils;
using Xunit;

namespace Kiriha.Tests;

public class Wave3BugfixTests
{
    private sealed class DummyLocalizer : ILocalizer
    {
        public string GetLoc(string key) => key;
        public string GetLoc(string key, params object?[] args) => string.Format(System.Globalization.CultureInfo.InvariantCulture, key, args);
    }

    // ─── Feature 14: Settings Split-Brain Isolation & Multi-Process Architecture ───

    [Fact]
    public void SettingsService_ApiUpdate_DoesNotOverwritePlayerSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Kiriha_Wave3_Settings_" + Guid.NewGuid());
        try
        {
            // 1. Initial write with volume = 85
            using (var service = new SettingsService(tempDir))
            {
                service.Update(s => s.Player.Volume = 85, SettingsSection.Player, save: true);
            }

            var playerFile = Path.Combine(tempDir, AppConstants.System.FileNames.SettingsPlayer);
            Assert.True(File.Exists(playerFile));
            var initialJson = File.ReadAllText(playerFile);
            Assert.Contains("85", initialJson);

            // 2. Simulate player process modifying player.json on disk to volume = 42
            var modifiedJson = initialJson.Replace("85", "42");
            File.WriteAllText(playerFile, modifiedJson);

            // 3. Main process runs API token update with scoped SettingsSection.Api
            using (var mainService = new SettingsService(tempDir))
            {
                mainService.Update(s => s.Api.Mal = new MalTokens { AccessToken = "tok123" }, SettingsSection.Api, save: true);
            }

            // 4. Verify player.json on disk was NOT clobbered with stale in-memory volume 85
            var afterApiUpdateJson = File.ReadAllText(playerFile);
            Assert.Contains("42", afterApiUpdateJson);
            Assert.DoesNotContain("85", afterApiUpdateJson);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PlayerProcessHelper_ArgumentList_PreservesQuotedTitles()
    {
        const string quotedTitleRu = "\"Звёздное дитя\" - Oshi no Ko";
        const string quotedOriginalTitle = "\"Oshi no Ko\"";

        var startInfo = PlayerProcessHelper.CreatePlayerStartInfo(
            processPath: "dotnet.exe",
            assemblyPath: "Kiriha.dll",
            videoUrl: "https://example.com/stream/episode1.mkv",
            titleRu: quotedTitleRu,
            titleEn: "Oshi no Ko",
            originalTitle: quotedOriginalTitle,
            episode: "01",
            animeId: 10452,
            torrentHash: "abcdef1234567890",
            subtitleUrl: "https://example.com/subs/01.ass",
            headers: new Dictionary<string, string> { { "Referer", "https://example.com" } });

        Assert.Contains("--player", startInfo.ArgumentList);
        Assert.Contains("https://example.com/stream/episode1.mkv", startInfo.ArgumentList);

        var titleRuIdx = startInfo.ArgumentList.IndexOf("--title-ru");
        Assert.True(titleRuIdx >= 0);
        Assert.Equal(quotedTitleRu, startInfo.ArgumentList[titleRuIdx + 1]);

        var originalTitleIdx = startInfo.ArgumentList.IndexOf("--original-title");
        Assert.True(originalTitleIdx >= 0);
        Assert.Equal(quotedOriginalTitle, startInfo.ArgumentList[originalTitleIdx + 1]);

        var episodeIdx = startInfo.ArgumentList.IndexOf("--episode");
        Assert.True(episodeIdx >= 0);
        Assert.Equal("01", startInfo.ArgumentList[episodeIdx + 1]);

        var animeIdIdx = startInfo.ArgumentList.IndexOf("--anime-id");
        Assert.True(animeIdIdx >= 0);
        Assert.Equal("10452", startInfo.ArgumentList[animeIdIdx + 1]);

        var subIdx = startInfo.ArgumentList.IndexOf("--sub-file");
        Assert.True(subIdx >= 0);
        Assert.Equal("https://example.com/subs/01.ass", startInfo.ArgumentList[subIdx + 1]);

        var headerIdx = startInfo.ArgumentList.IndexOf("--http-header");
        Assert.True(headerIdx >= 0);
        Assert.Equal("Referer=https://example.com", startInfo.ArgumentList[headerIdx + 1]);
    }

    // ─── Feature 15 & 17: Player Controls, Natural Sorting & Hotkeys ─────────

    [Fact]
    public void NaturalSortComparer_CorrectlyOrdersNumericAndChunkedFilenames()
    {
        var comparer = NaturalSortComparer.Instance;

        // 1. Basic numeric ordering
        var files = new[] { "10.mkv", "1.mkv", "2.mkv", "20.mkv" };
        var sorted = files.OrderBy(x => x, comparer).ToArray();
        Assert.Equal(new[] { "1.mkv", "2.mkv", "10.mkv", "20.mkv" }, sorted);

        // 2. Leading zeros
        var leadingZeros = new[] { "Episode 02.mkv", "Episode 1.mkv", "Episode 001.mkv", "Episode 10.mkv" };
        var sortedLeading = leadingZeros.OrderBy(x => x, comparer).ToArray();
        Assert.Equal("Episode 1.mkv", sortedLeading[0]);
        Assert.Equal("Episode 001.mkv", sortedLeading[1]);
        Assert.Equal("Episode 02.mkv", sortedLeading[2]);
        Assert.Equal("Episode 10.mkv", sortedLeading[3]);

        // 3. Multi-chunk titles with season and episode numbers
        var multiChunk = new[] { "Season 2 - 10", "Season 1 - 02", "Season 1 - 01", "Season 2 - 01" };
        var sortedMulti = multiChunk.OrderBy(x => x, comparer).ToArray();
        Assert.Equal(new[] { "Season 1 - 01", "Season 1 - 02", "Season 2 - 01", "Season 2 - 10" }, sortedMulti);

        // 4. Null & identity handling
        Assert.True(comparer.Compare(null, "a") < 0);
        Assert.True(comparer.Compare("a", null) > 0);
        Assert.Equal(0, comparer.Compare(null, null));
        Assert.Equal(0, comparer.Compare("same", "same"));
    }

    [Fact]
    public void PlayerTimelineScrub_ResetsIsScrubbingEvenWithoutAttachedPlayer()
    {
        var timeline = new PlayerTimelineService();
        timeline.BeginScrub();
        Assert.True(timeline.IsScrubbing);

        var snapshot = timeline.EndScrub(125.0);
        Assert.False(timeline.IsScrubbing);
        Assert.Equal(125.0, snapshot.CurrentTime);

        // Verify with PlayerViewModel without libmpv native attachment
        var localizer = new DummyLocalizer();
        var vm = new PlayerViewModel("test.mkv", null, null, null, localizer);
        vm.BeginScrub();
        Assert.True(vm.IsScrubbing);

        vm.EndScrub();
        Assert.False(vm.IsScrubbing);
    }

    [Fact]
    public void PlayerViewModel_HotkeyCallbacks_PersistToSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Kiriha_Wave3_Hotkeys_" + Guid.NewGuid());
        try
        {
            using var settingsService = new SettingsService(tempDir);
            var localizer = new DummyLocalizer();
            var vm = new PlayerViewModel("test.mkv", null, null, settingsService, localizer);

            vm.ReloadSubtitlesHotkey = "Ctrl+R";
            Assert.Equal("Ctrl+R", settingsService.Current.Player.ReloadSubtitlesHotkey);

            vm.FrameStepForwardHotkey = "Right";
            Assert.Equal("Right", settingsService.Current.Player.FrameStepForwardHotkey);

            vm.FrameStepBackwardHotkey = "Left";
            Assert.Equal("Left", settingsService.Current.Player.FrameStepBackwardHotkey);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ─── Feature 16 & 17: Mpv, OpenGL, Thumbnailer & Pipeline Sanitization ───

    [Fact]
    public void MpvOptions_Sanitize_ReplacesIncompatibleGpuApisWithAutoAndForcesLibmpv()
    {
        // d3d11 is sanitized to auto
        var d3dOptions = new MpvOptions("auto", "gpu-next", "d3d11", "dxgi");
        var sanitizedD3D = MpvOptions.Sanitize(d3dOptions);
        Assert.Equal("libmpv", sanitizedD3D.VideoOutput);
        Assert.Equal("auto", sanitizedD3D.GpuApi);
        Assert.Equal("auto", sanitizedD3D.GpuContext);

        // vulkan is sanitized to auto
        var vulkanOptions = new MpvOptions("auto", "gpu", "vulkan", "winvk");
        var sanitizedVulkan = MpvOptions.Sanitize(vulkanOptions);
        Assert.Equal("libmpv", sanitizedVulkan.VideoOutput);
        Assert.Equal("auto", sanitizedVulkan.GpuApi);
        Assert.Equal("auto", sanitizedVulkan.GpuContext);

        // opengl is preserved
        var glOptions = new MpvOptions("auto", "gpu", "opengl", "win");
        var sanitizedGl = MpvOptions.Sanitize(glOptions);
        Assert.Equal("libmpv", sanitizedGl.VideoOutput);
        Assert.Equal("opengl", sanitizedGl.GpuApi);
        Assert.Equal("auto", sanitizedGl.GpuContext);
    }

    [Fact]
    public void SmartTrackAutoloader_SubfolderMatching_MatchesTracksInSubfolders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Kiriha_Wave3_Autoload_" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tempDir);
            var subsDir = Path.Combine(tempDir, "Subs");
            Directory.CreateDirectory(subsDir);

            var videoFile = Path.Combine(tempDir, "[Group] Frieren - Beyond Journey's End - 01 [1080p].mkv");
            File.WriteAllText(videoFile, "dummy video file");

            var subMatch = Path.Combine(subsDir, "[Group] Frieren - Beyond Journey's End - 01.ass");
            var subOther = Path.Combine(subsDir, "[Group] Frieren - Beyond Journey's End - 02.ass");
            File.WriteAllText(subMatch, "dummy subtitle file 1");
            File.WriteAllText(subOther, "dummy subtitle file 2");

            var matches = SmartTrackAutoloader.FindMatchingTracks(videoFile);
            Assert.Contains(matches.SubtitlePaths, s => s.EndsWith("01.ass"));
            Assert.DoesNotContain(matches.SubtitlePaths, s => s.EndsWith("02.ass"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MpvPropertyCache_Reset_ClearsPlaybackStateAndDiagnostics()
    {
        var cache = new MpvPropertyCache(new MpvRuntimeDiagnostics { HwDecVo = "opengl", CodecDecoder = "h264" });

        cache.TryUpdateTimePosition(145.2);
        cache.TryUpdateDuration(420.0);
        cache.TryUpdatePause(false);
        cache.TryUpdateSeekable(true);
        cache.TryUpdateLoaded(true);

        Assert.Equal(145.2, cache.TimePosition);
        Assert.Equal(420.0, cache.Duration);
        Assert.False(cache.IsPaused);
        Assert.True(cache.PlaybackState.IsLoaded);
        Assert.True(cache.PlaybackState.IsSeekable);

        cache.Reset();

        Assert.Equal(0, cache.TimePosition);
        Assert.Equal(0, cache.Duration);
        Assert.True(cache.IsPaused);
        Assert.False(cache.PlaybackState.IsLoaded);
        Assert.False(cache.PlaybackState.IsSeekable);
    }
}
