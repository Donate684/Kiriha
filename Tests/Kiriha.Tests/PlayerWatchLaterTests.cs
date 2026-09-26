using System;
using System.IO;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Platform;
using Kiriha.Mpv;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Mpv.UI.Views.Converters;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class PlayerWatchLaterTests
{
    [Fact]
    public void PlayerConfig_RememberPlaybackPosition_DefaultsToTrue()
    {
        var config = new AppSettings.PlayerConfig();
        Assert.True(config.RememberPlaybackPosition);
    }

    [Fact]
    public void MpvOptions_SavePositionOnQuit_DefaultsToTrue()
    {
        var opts = MpvOptions.Default;
        Assert.True(opts.SavePositionOnQuit);
        Assert.Null(opts.WatchLaterDirectory);
    }

    [Fact]
    public void PathHelper_GetMpvWatchLaterPath_ReturnsCorrectSubdirectory()
    {
        var path = PathHelper.GetMpvWatchLaterPath();
        Assert.NotNull(path);
        Assert.EndsWith(AppConstants.System.FileNames.MpvWatchLaterDir, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingModifiedConverter_HandlesRememberPlaybackPositionCorrectly()
    {
        var converter = new SettingModifiedConverter();

        // When RememberPlaybackPosition is true (default), button should not show (returns false)
        var isModifiedWhenTrue = (bool)converter.Convert(true, typeof(bool), "True", System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.False(isModifiedWhenTrue);

        // When RememberPlaybackPosition is false (modified), button should show (returns true)
        var isModifiedWhenFalse = (bool)converter.Convert(false, typeof(bool), "True", System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.True(isModifiedWhenFalse);
    }

    [Fact]
    public void ResetSetting_RememberPlaybackPosition_ResetsToDefault()
    {
        var settingsMock = new Mock<ISettingsService>();
        var settings = new AppSettings();
        settingsMock.Setup(s => s.Current).Returns(settings);

        var localizerMock = new Mock<ILocalizer>();
        localizerMock.Setup(l => l.GetLoc(It.IsAny<string>())).Returns("Test");

        var vm = new PlayerViewModel(
            "test.mkv",
            null,
            null,
            settingsMock.Object,
            localizerMock.Object);

        vm.RememberPlaybackPosition = false;
        Assert.False(vm.RememberPlaybackPosition);

        vm.ResetSetting("RememberPlaybackPosition");
        Assert.True(vm.RememberPlaybackPosition);
    }

    [Fact]
    public void ResetPlaybackSettings_ResetsRememberPlaybackPosition()
    {
        var settingsMock = new Mock<ISettingsService>();
        var settings = new AppSettings();
        settingsMock.Setup(s => s.Current).Returns(settings);

        var localizerMock = new Mock<ILocalizer>();
        localizerMock.Setup(l => l.GetLoc(It.IsAny<string>())).Returns("Test");

        var vm = new PlayerViewModel(
            "test.mkv",
            null,
            null,
            settingsMock.Object,
            localizerMock.Object);

        vm.RememberPlaybackPosition = false;
        Assert.False(vm.RememberPlaybackPosition);

        vm.ResetPlaybackSettings();
        Assert.True(vm.RememberPlaybackPosition);
    }

    [Fact]
    public void MpvPlayerBuilder_Build_WithWatchLaterOptions_InitializesSuccessfully()
    {
        var watchLaterDir = Path.Combine(Path.GetTempPath(), "kiriha_test_watch_later_" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new MpvOptions(
                "no",
                "null",
                "auto",
                "auto",
                SavePositionOnQuit: true,
                WatchLaterDirectory: watchLaterDir);

            using var player = MpvPlayerBuilder.Build(options);
            Assert.NotNull(player);
            Assert.True(Directory.Exists(watchLaterDir));
        }
        finally
        {
            if (Directory.Exists(watchLaterDir))
            {
                try { Directory.Delete(watchLaterDir, true); } catch { }
            }
        }
    }
}
