using Kiriha.Services.Data.Settings;
using System;
using System.Linq;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Models.Presentation;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    partial void OnPlayerAutoPlayChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.AutoPlay = value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnSinglePlayerWindowChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SingleWindow = value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnRememberPlayerVolumeChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings =>
        {
            settings.Player.RememberVolume = value;
            if (value) settings.Player.Volume = Math.Clamp(Volume, 0, 100);
        }, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnAutoHideControlsChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.AutoHideControls = value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnAutoHideTimeoutChanged(double value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.AutoHideTimeout = value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnShowChapterMarkersChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ShowChapterMarkers = value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnLeftClickActionChanged(PlayerMouseActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.LeftClickAction = value.Value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnRightClickActionChanged(PlayerMouseActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.RightClickAction = value.Value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnMiddleClickActionChanged(PlayerMouseActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.MiddleClickAction = value.Value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnWheelUpActionChanged(PlayerWheelActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.WheelUpAction = value.Value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnWheelDownActionChanged(PlayerWheelActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.WheelDownAction = value.Value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnWheelVolumeStepChanged(int value)
    {
        var normalized = FindWheelStep(value);
        if (value != normalized)
        {
            WheelVolumeStep = normalized;
            return;
        }

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.WheelVolumeStep = normalized, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnSeekStepChanged(int value)
    {
        var normalized = FindSeekStep(value);
        if (value != normalized)
        {
            SeekStep = normalized;
            return;
        }

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SeekStep = normalized, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnShowPlayPauseButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowPlayPauseButton = value);
    partial void OnShowSkipButtonsChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSkipButtons = value);
    partial void OnShowMuteButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowMuteButton = value);
    partial void OnShowVolumeSliderChanged(bool value) => SavePlayerPanelButtons(x => x.ShowVolumeSlider = value);
    partial void OnShowTimeDisplayChanged(bool value) => SavePlayerPanelButtons(x => x.ShowTimeDisplay = value);
    partial void OnShowSpeedButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSpeedButton = value);
    partial void OnShowSubtitleButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSubtitleButton = value);
    partial void OnShowSubtitlePositionButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSubtitlePositionButton = value);
    partial void OnShowAudioButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowAudioButton = value);
    partial void OnShowScreenshotButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowScreenshotButton = value);
    partial void OnShowSubtitleStyleButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSubtitleStyleButton = value);
    
    partial void OnPreferredAudioLanguagesChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        PreferredAudioLanguages = NormalizeLanguageList(value, "Japanese,jpn,ja");
        _settingsService.Update(settings => settings.Player.PreferredAudioLanguages = PreferredAudioLanguages, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
        ApplyTrackLanguagePreferences();
    }

    private void SavePlayerPanelButtons(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    private void ApplyTrackLanguagePreferences()
    {
        _settingsApplier.ApplyTrackLanguagePreferences(new PlayerTrackLanguageOptions(
            NormalizeLanguageList(PreferredAudioLanguages, "Japanese,jpn,ja"),
            NormalizeLanguageList(PreferredSubtitleLanguages, "Russian,rus,ru")));
    }

    private static string NormalizeLanguageList(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length == 0 ? fallback : string.Join(",", parts);
    }

    private static string NormalizeHotkey(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
            Kiriha.Utils.PowerManager.KeepDisplayActive();
        else
            Kiriha.Utils.PowerManager.AllowDisplaySleep();
    }
}
