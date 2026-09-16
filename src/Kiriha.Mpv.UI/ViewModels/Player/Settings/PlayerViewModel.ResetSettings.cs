using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    private static readonly AppSettings.PlayerConfig DefaultPlayerConfig = new();

    [RelayCommand]
    public void ResetSetting(string settingKey)
    {
        var def = DefaultPlayerConfig;
        switch (settingKey)
        {
            // Playback
            case "PlayerAutoPlay":
                PlayerAutoPlay = def.AutoPlay;
                break;
            case "SinglePlayerWindow":
                SinglePlayerWindow = def.SingleWindow;
                break;
            case "PlaybackSpeed":
                PlaybackSpeed = def.PlaybackSpeed;
                break;

            // Audio
            case "RememberPlayerVolume":
                RememberPlayerVolume = def.RememberVolume;
                break;
            case "NormalizeAudio":
                NormalizeAudio = def.NormalizeAudio;
                break;
            case "Volume":
                Volume = def.Volume;
                break;

            // Video
            case "MpvVideoPreset":
                MpvVideoPreset = def.MpvVideoPreset;
                break;
            case "MpvScale":
                MpvScale = def.MpvScale;
                break;
            case "MpvChromaScale":
                MpvChromaScale = def.MpvChromaScale;
                break;
            case "MpvDitherDepth":
                MpvDitherDepth = def.MpvDitherDepth;
                break;
            case "MpvCorrectDownscaling":
                MpvCorrectDownscaling = def.MpvCorrectDownscaling;
                break;
            case "MpvDeband":
                MpvDeband = def.MpvDeband;
                break;
            case "MpvDebandIterations":
                MpvDebandIterations = def.MpvDebandIterations;
                break;
            case "MpvDebandThreshold":
                MpvDebandThreshold = def.MpvDebandThreshold;
                break;
            case "MpvHwdec":
                MpvHwdec = def.MpvHwdec;
                break;
            case "MpvVideoOutput":
                MpvVideoOutput = def.MpvVideoOutput;
                break;
            case "MpvGpuApi":
                MpvGpuApi = def.MpvGpuApi;
                break;
            case "MpvGpuContext":
                MpvGpuContext = def.MpvGpuContext;
                break;

            // Track
            case "PreferredAudioLanguages":
                PreferredAudioLanguages = def.PreferredAudioLanguages;
                break;
            case "PreferredSubtitleLanguages":
                PreferredSubtitleLanguages = def.PreferredSubtitleLanguages;
                break;
            case "SmartTrackAutoload":
                SmartTrackAutoload = def.SmartTrackAutoload;
                break;

            // Interface
            case "AutoHideControls":
                AutoHideControls = def.AutoHideControls;
                break;
            case "AutoHideTimeout":
                AutoHideTimeout = def.AutoHideTimeout;
                break;
            case "ShowChapterMarkers":
                ShowChapterMarkers = def.ShowChapterMarkers;
                break;
            case "LeftClickAction":
                LeftClickAction = FindMouseAction(def.LeftClickAction);
                break;
            case "RightClickAction":
                RightClickAction = FindMouseAction(def.RightClickAction);
                break;
            case "MiddleClickAction":
                MiddleClickAction = FindMouseAction(def.MiddleClickAction);
                break;
            case "WheelUpAction":
                WheelUpAction = FindWheelAction(def.WheelUpAction);
                break;
            case "WheelDownAction":
                WheelDownAction = FindWheelAction(def.WheelDownAction);
                break;
            case "WheelVolumeStep":
                WheelVolumeStep = FindWheelStep(def.WheelVolumeStep);
                break;
            case "SeekStep":
                SeekStep = FindSeekStep(def.SeekStep);
                break;
            case "TogglePlayPauseHotkey":
                TogglePlayPauseHotkey = def.TogglePlayPauseHotkey;
                break;
            case "ToggleFullscreenHotkey":
                ToggleFullscreenHotkey = def.ToggleFullscreenHotkey;
                break;
            case "ExitFullscreenHotkey":
                ExitFullscreenHotkey = def.ExitFullscreenHotkey;
                break;
            case "ToggleMuteHotkey":
                ToggleMuteHotkey = def.ToggleMuteHotkey;
                break;
            case "CycleAudioHotkey":
                CycleAudioHotkey = def.CycleAudioHotkey;
                break;
            case "CycleSubtitleHotkey":
                CycleSubtitleHotkey = def.CycleSubtitleHotkey;
                break;
            case "PreviousMediaHotkey":
                PreviousMediaHotkey = def.PreviousMediaHotkey;
                break;
            case "NextMediaHotkey":
                NextMediaHotkey = def.NextMediaHotkey;
                break;
            case "SpeedDownHotkey":
                SpeedDownHotkey = def.SpeedDownHotkey;
                break;
            case "SpeedUpHotkey":
                SpeedUpHotkey = def.SpeedUpHotkey;
                break;
            case "VolumeUpHotkey":
                VolumeUpHotkey = def.VolumeUpHotkey;
                break;
            case "VolumeDownHotkey":
                VolumeDownHotkey = def.VolumeDownHotkey;
                break;
            case "SeekBackwardHotkey":
                SeekBackwardHotkey = def.SeekBackwardHotkey;
                break;
            case "SeekForwardHotkey":
                SeekForwardHotkey = def.SeekForwardHotkey;
                break;
            case "ReloadSubtitlesHotkey":
                ReloadSubtitlesHotkey = def.ReloadSubtitlesHotkey;
                break;
            case "FrameStepForwardHotkey":
                FrameStepForwardHotkey = def.FrameStepForwardHotkey;
                break;
            case "FrameStepBackwardHotkey":
                FrameStepBackwardHotkey = def.FrameStepBackwardHotkey;
                break;
            case "ShowPlayPauseButton":
                ShowPlayPauseButton = def.ShowPlayPauseButton;
                break;
            case "ShowSkipButtons":
                ShowSkipButtons = def.ShowSkipButtons;
                break;
            case "ShowMuteButton":
                ShowMuteButton = def.ShowMuteButton;
                break;
            case "ShowVolumeSlider":
                ShowVolumeSlider = def.ShowVolumeSlider;
                break;
            case "ShowTimeDisplay":
                ShowTimeDisplay = def.ShowTimeDisplay;
                break;
            case "ShowSpeedButton":
                ShowSpeedButton = def.ShowSpeedButton;
                break;
            case "ShowSubtitleButton":
                ShowSubtitleButton = def.ShowSubtitleButton;
                break;
            case "ShowSubtitlePositionButton":
                ShowSubtitlePositionButton = def.ShowSubtitlePositionButton;
                break;
            case "ShowAudioButton":
                ShowAudioButton = def.ShowAudioButton;
                break;
            case "ShowScreenshotButton":
                ShowScreenshotButton = def.ShowScreenshotButton;
                break;
            case "ShowSubtitleStyleButton":
                ShowSubtitleStyleButton = def.ShowSubtitleStyleButton;
                break;
            case "SubtitleStyleHotkey":
                SubtitleStyleHotkey = def.SubtitleStyleHotkey;
                break;

            // Advanced
            case "ScreenshotDirectory":
                ScreenshotDirectory = def.ScreenshotDirectory;
                break;
            case "ScreenshotFormat":
                ScreenshotFormat = def.ScreenshotFormat;
                break;
            case "ScreenshotResolution":
                ScreenshotResolution = FindScreenshotResolution(def.ScreenshotResolutionMode);
                break;
            case "ScreenshotPngCompression":
                ScreenshotPngCompression = def.ScreenshotPngCompression;
                break;
            case "ScreenshotQuality":
                ScreenshotQuality = def.ScreenshotQuality;
                break;
            case "ScreenshotHighBitDepth":
                ScreenshotHighBitDepth = def.ScreenshotHighBitDepth;
                break;
            case "ScreenshotWithSubtitlesHotkey":
                ScreenshotWithSubtitlesHotkey = def.ScreenshotWithSubtitlesHotkey;
                break;
            case "ScreenshotWithoutSubtitlesHotkey":
                ScreenshotWithoutSubtitlesHotkey = def.ScreenshotWithoutSubtitlesHotkey;
                break;
            case "SubtitleStyleOverrideEnabled":
                SubtitleStyleOverrideEnabled = def.SubtitleStyleOverrideEnabled;
                break;
            case "SubtitleFont":
                SubtitleFont = def.SubtitleFont;
                break;
            case "SubtitleFontSize":
                SubtitleFontSize = def.SubtitleFontSize;
                break;
            case "SubtitleColor":
                SubtitleColor = def.SubtitleColor;
                break;
            case "SubtitleBorderColor":
                SubtitleBorderColor = def.SubtitleBorderColor;
                break;
            case "SubtitleShadowColor":
                SubtitleShadowColor = def.SubtitleShadowColor;
                break;
            case "SubtitleBorderSize":
                SubtitleBorderSize = def.SubtitleBorderSize;
                break;
            case "SubtitleShadowOffset":
                SubtitleShadowOffset = def.SubtitleShadowOffset;
                break;
            case "SubtitleMarginY":
                SubtitleMarginY = def.SubtitleMarginY;
                break;
            case "SubtitleScaleByWindow":
                SubtitleScaleByWindow = def.SubtitleScaleByWindow;
                break;
            case "EnableSonokoIntegration":
                EnableSonokoIntegration = def.EnableSonokoIntegration;
                break;
            case "SonokoIntegrationHotkey":
                SonokoIntegrationHotkey = def.SonokoIntegrationHotkey;
                break;
        }
    }

    [RelayCommand]
    public void ResetPlaybackSettings()
    {
        ResetSetting("PlayerAutoPlay");
        ResetSetting("SinglePlayerWindow");
        ResetSetting("PlaybackSpeed");
    }

    [RelayCommand]
    public void ResetAudioSettings()
    {
        ResetSetting("RememberPlayerVolume");
        ResetSetting("NormalizeAudio");
        ResetSetting("Volume");
    }

    [RelayCommand]
    public void ResetVideoSettings()
    {
        ResetSetting("MpvVideoPreset");
        ResetSetting("MpvScale");
        ResetSetting("MpvChromaScale");
        ResetSetting("MpvDitherDepth");
        ResetSetting("MpvCorrectDownscaling");
        ResetSetting("MpvDeband");
        ResetSetting("MpvDebandIterations");
        ResetSetting("MpvDebandThreshold");
        ResetSetting("MpvHwdec");
        ResetSetting("MpvVideoOutput");
        ResetSetting("MpvGpuApi");
        ResetSetting("MpvGpuContext");
    }

    [RelayCommand]
    public void ResetTrackSettings()
    {
        ResetSetting("PreferredAudioLanguages");
        ResetSetting("PreferredSubtitleLanguages");
        ResetSetting("SmartTrackAutoload");
    }

    [RelayCommand]
    public void ResetInterfaceSettings()
    {
        ResetSetting("AutoHideControls");
        ResetSetting("AutoHideTimeout");
        ResetSetting("ShowChapterMarkers");
        ResetSetting("LeftClickAction");
        ResetSetting("RightClickAction");
        ResetSetting("MiddleClickAction");
        ResetSetting("WheelUpAction");
        ResetSetting("WheelDownAction");
        ResetSetting("WheelVolumeStep");
        ResetSetting("SeekStep");
        ResetSetting("TogglePlayPauseHotkey");
        ResetSetting("ToggleFullscreenHotkey");
        ResetSetting("ExitFullscreenHotkey");
        ResetSetting("ToggleMuteHotkey");
        ResetSetting("CycleAudioHotkey");
        ResetSetting("CycleSubtitleHotkey");
        ResetSetting("PreviousMediaHotkey");
        ResetSetting("NextMediaHotkey");
        ResetSetting("SpeedDownHotkey");
        ResetSetting("SpeedUpHotkey");
        ResetSetting("VolumeUpHotkey");
        ResetSetting("VolumeDownHotkey");
        ResetSetting("SeekBackwardHotkey");
        ResetSetting("SeekForwardHotkey");
        ResetSetting("ReloadSubtitlesHotkey");
        ResetSetting("FrameStepForwardHotkey");
        ResetSetting("FrameStepBackwardHotkey");
        ResetSetting("ShowPlayPauseButton");
        ResetSetting("ShowSkipButtons");
        ResetSetting("ShowMuteButton");
        ResetSetting("ShowVolumeSlider");
        ResetSetting("ShowTimeDisplay");
        ResetSetting("ShowSpeedButton");
        ResetSetting("ShowSubtitleButton");
        ResetSetting("ShowSubtitlePositionButton");
        ResetSetting("ShowAudioButton");
        ResetSetting("ShowScreenshotButton");
        ResetSetting("ShowSubtitleStyleButton");
        ResetSetting("SubtitleStyleHotkey");
    }

    [RelayCommand]
    public void ResetAdvancedSettings()
    {
        ResetSetting("ScreenshotDirectory");
        ResetSetting("ScreenshotFormat");
        ResetSetting("ScreenshotResolution");
        ResetSetting("ScreenshotPngCompression");
        ResetSetting("ScreenshotQuality");
        ResetSetting("ScreenshotHighBitDepth");
        ResetSetting("ScreenshotWithSubtitlesHotkey");
        ResetSetting("ScreenshotWithoutSubtitlesHotkey");
        ResetSetting("SubtitleStyleOverrideEnabled");
        ResetSetting("SubtitleFont");
        ResetSetting("SubtitleFontSize");
        ResetSetting("SubtitleColor");
        ResetSetting("SubtitleBorderColor");
        ResetSetting("SubtitleShadowColor");
        ResetSetting("SubtitleBorderSize");
        ResetSetting("SubtitleShadowOffset");
        ResetSetting("SubtitleMarginY");
        ResetSetting("SubtitleScaleByWindow");
        ResetSetting("EnableSonokoIntegration");
        ResetSetting("SonokoIntegrationHotkey");
    }

    [RelayCommand]
    public void ResetAllPlayerSettings()
    {
        ResetPlaybackSettings();
        ResetAudioSettings();
        ResetVideoSettings();
        ResetTrackSettings();
        ResetInterfaceSettings();
        ResetAdvancedSettings();
    }
}
