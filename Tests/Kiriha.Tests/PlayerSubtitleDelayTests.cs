using System.Globalization;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.ViewModels.Player;
using Xunit;

namespace Kiriha.Tests;

public sealed class PlayerSubtitleDelayTests
{
    private sealed class DummyLocalizer : ILocalizer
    {
        public string GetLoc(string key) => key switch
        {
            "player.osd.sub_delay" => "Sub delay",
            "player.osd.ms" => "ms",
            _ => key
        };

        public string GetLoc(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, GetLoc(key), args);
    }

    [Fact]
    public void PlayerConfig_DefaultSubtitleDelayHotkeys_AreZAndX()
    {
        var config = new AppSettings.PlayerConfig();

        Assert.Equal("Z", config.SubtitleDelayEarlierHotkey);
        Assert.Equal("X", config.SubtitleDelayLaterHotkey);
    }

    [Fact]
    public void PlayerViewModel_AdjustSubtitleDelay_UpdatesOsdAccurately()
    {
        var localizer = new DummyLocalizer();
        var vm = new PlayerViewModel(
            videoUrl: "test.mkv",
            metadata: null,
            metadataResolver: null,
            settingsService: null,
            localizer: localizer);

        // Act - Shift 100ms earlier (-0.1s)
        vm.AdjustSubtitleDelay(-0.1);

        Assert.True(vm.Overlay.IsOsdVisible);
        Assert.Equal("Sub delay", vm.Overlay.OsdMessage);
        Assert.Equal("-100 ms", vm.Overlay.OsdDetail);

        // Act - Shift another 100ms earlier
        vm.AdjustSubtitleDelay(-0.1);
        Assert.Equal("-200 ms", vm.Overlay.OsdDetail);

        // Act - Shift 100ms later (+0.1s)
        vm.AdjustSubtitleDelay(0.1);
        Assert.Equal("-100 ms", vm.Overlay.OsdDetail);

        // Act - Shift 100ms later (+0.1s) -> back to 0
        vm.AdjustSubtitleDelay(0.1);
        Assert.Equal("0 ms", vm.Overlay.OsdDetail);

        // Act - Shift 100ms later (+0.1s) -> +100
        vm.AdjustSubtitleDelay(0.1);
        Assert.Equal("+100 ms", vm.Overlay.OsdDetail);
    }

    [Fact]
    public void PlayerViewModel_ResetSetting_ResetsSubtitleDelayHotkeys()
    {
        var localizer = new DummyLocalizer();
        var vm = new PlayerViewModel(
            videoUrl: "test.mkv",
            metadata: null,
            metadataResolver: null,
            settingsService: null,
            localizer: localizer);

        vm.SubtitleDelayEarlierHotkey = "Ctrl+Shift+Z";
        vm.SubtitleDelayLaterHotkey = "Ctrl+Shift+X";

        vm.ResetSetting("SubtitleDelayEarlierHotkey");
        vm.ResetSetting("SubtitleDelayLaterHotkey");

        Assert.Equal("Z", vm.SubtitleDelayEarlierHotkey);
        Assert.Equal("X", vm.SubtitleDelayLaterHotkey);
    }
}
