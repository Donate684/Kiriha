using Kiriha.Services.Data.Settings;
using System;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    partial void OnTogglePlayPauseHotkeyChanged(string value)
    {
        SaveHotkey(value, "Space", (settings, hotkey) => settings.TogglePlayPauseHotkey = hotkey);
    }
    partial void OnToggleFullscreenHotkeyChanged(string value)
    {
        SaveHotkey(value, "F", (settings, hotkey) => settings.ToggleFullscreenHotkey = hotkey);
    }
    partial void OnExitFullscreenHotkeyChanged(string value)
    {
        SaveHotkey(value, "Escape", (settings, hotkey) => settings.ExitFullscreenHotkey = hotkey);
    }
    partial void OnToggleMuteHotkeyChanged(string value)
    {
        SaveHotkey(value, "M", (settings, hotkey) => settings.ToggleMuteHotkey = hotkey);
    }
    partial void OnCycleAudioHotkeyChanged(string value)
    {
        SaveHotkey(value, "A", (settings, hotkey) => settings.CycleAudioHotkey = hotkey);
    }
    partial void OnCycleSubtitleHotkeyChanged(string value)
    {
        SaveHotkey(value, "V", (settings, hotkey) => settings.CycleSubtitleHotkey = hotkey);
    }
    partial void OnPreviousMediaHotkeyChanged(string value)
    {
        SaveHotkey(value, "P", (settings, hotkey) => settings.PreviousMediaHotkey = hotkey);
    }
    partial void OnNextMediaHotkeyChanged(string value)
    {
        SaveHotkey(value, "N", (settings, hotkey) => settings.NextMediaHotkey = hotkey);
    }
    partial void OnSpeedDownHotkeyChanged(string value)
    {
        SaveHotkey(value, "OemOpenBrackets", (settings, hotkey) => settings.SpeedDownHotkey = hotkey);
    }
    partial void OnSpeedUpHotkeyChanged(string value)
    {
        SaveHotkey(value, "OemCloseBrackets", (settings, hotkey) => settings.SpeedUpHotkey = hotkey);
    }
    partial void OnVolumeUpHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.VolumeUpHotkey = NormalizeHotkey(value, "Up"), Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }
    partial void OnVolumeDownHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.VolumeDownHotkey = NormalizeHotkey(value, "Down"), Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }
    partial void OnSeekBackwardHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SeekBackwardHotkey = NormalizeHotkey(value, "Left"), Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }
    partial void OnSeekForwardHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SeekForwardHotkey = NormalizeHotkey(value, "Right"), Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    private void SaveHotkey(string value, string fallback, Action<AppSettings.PlayerConfig, string> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        var normalized = NormalizeHotkey(value, fallback);
        _settingsService.Update(settings => update(settings.Player, normalized), Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnEnableSonokoIntegrationChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.EnableSonokoIntegration = value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    partial void OnSonokoIntegrationHotkeyChanged(string value)
    {
        SaveHotkey(value, "T", (settings, hotkey) => settings.SonokoIntegrationHotkey = hotkey);
    }
}
