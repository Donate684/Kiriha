using Kiriha.Services.Data.Settings;
using System.IO;
using Kiriha.Models.Presentation;
using System;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    partial void OnScreenshotDirectoryChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        var normalized = NormalizeScreenshotDirectory(value);
        _settingsService.Update(settings => settings.Player.ScreenshotDirectory = normalized, Kiriha.Core.Services.SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotFormatChanged(string value)
    {
        var normalized = NormalizeScreenshotFormat(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            ScreenshotFormat = normalized;

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotFormat = normalized, Kiriha.Core.Services.SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotResolutionChanged(ScreenshotResolutionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotResolutionMode = value.Value, Kiriha.Core.Services.SettingsSection.Player);
    }
    partial void OnScreenshotPngCompressionChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 9);
        if (value != normalized)
        {
            ScreenshotPngCompression = normalized;
            return;
        }

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotPngCompression = normalized, Kiriha.Core.Services.SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotQualityChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 100);
        if (value != normalized)
        {
            ScreenshotQuality = normalized;
            return;
        }

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotQuality = normalized, Kiriha.Core.Services.SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotHighBitDepthChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotHighBitDepth = value, Kiriha.Core.Services.SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotWithSubtitlesHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotWithSubtitlesHotkey = NormalizeHotkey(value, "S"), Kiriha.Core.Services.SettingsSection.Player);
    }
    partial void OnScreenshotWithoutSubtitlesHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotWithoutSubtitlesHotkey = NormalizeHotkey(value, "Shift+S"), Kiriha.Core.Services.SettingsSection.Player);
    }

    private void ApplyScreenshotOptions()
    {
        _settingsApplier.ApplyScreenshot(new PlayerScreenshotOptions(
            ScreenshotDirectory,
            ScreenshotFormat,
            ScreenshotPngCompression,
            ScreenshotQuality,
            ScreenshotHighBitDepth));
    }

    private static string NormalizeScreenshotDirectory(string? value)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(value))
            return desktop;

        var trimmed = value.Trim();
        var oldDefault = System.IO.Path.Combine(desktop, "Kiriha Screenshots");
        return string.Equals(trimmed, oldDefault, StringComparison.OrdinalIgnoreCase)
            ? desktop
            : trimmed;
    }

    private static string NormalizeScreenshotFormat(string? value)
    {
        if (string.Equals(value, "jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "jpeg", StringComparison.OrdinalIgnoreCase))
            return "jpg";

        if (string.Equals(value, "webp", StringComparison.OrdinalIgnoreCase))
            return "webp";

        return "png";
    }
}
