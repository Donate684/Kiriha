
using System;
using System.Linq;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    partial void OnPreferredSubtitleLanguagesChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        PreferredSubtitleLanguages = NormalizeLanguageList(value, "Russian,rus,ru");
        _settingsService.Update(settings => settings.Player.PreferredSubtitleLanguages = PreferredSubtitleLanguages, SettingsSection.Player);
        ApplyTrackLanguagePreferences();
    }
    partial void OnSubtitleStyleOverrideEnabledChanged(bool value)
    {
        if (!_isApplyingSettings)
            ShowOsd(_localizer.GetLoc("player.osd.subtitle_style"), value ? _localizer.GetLoc("player.osd.enabled") : _localizer.GetLoc("player.osd.disabled"));

        if (_isApplyingSettings) return;
        ApplySubtitleStyleOverride();
        if (_settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SubtitleStyleOverrideEnabled = value, SettingsSection.Player);
    }
    partial void OnSubtitleStyleHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SubtitleStyleHotkey = NormalizeHotkey(value, "U"), SettingsSection.Player);
    }
    partial void OnSubtitleFontChanged(string value) => SaveSubtitleStyle(x => x.SubtitleFont = NormalizeMpvOption(value, "Candara Bold"));
    partial void OnSubtitleFontSizeChanged(double value) => SaveSubtitleStyle(x => x.SubtitleFontSize = Math.Clamp(value, 1, 300));
    partial void OnSubtitleColorChanged(string value) => SaveSubtitleStyle(x => x.SubtitleColor = NormalizeSubtitleColor(value, "#FFFFFF"));
    partial void OnSubtitleBorderColorChanged(string value) => SaveSubtitleStyle(x => x.SubtitleBorderColor = NormalizeSubtitleColor(value, "#000000"));
    partial void OnSubtitleShadowColorChanged(string value) => SaveSubtitleStyle(x => x.SubtitleShadowColor = NormalizeSubtitleColor(value, "#000000"));
    partial void OnSubtitleBorderSizeChanged(double value) => SaveSubtitleStyle(x => x.SubtitleBorderSize = Math.Clamp(value, 0, 20));
    partial void OnSubtitleShadowOffsetChanged(double value) => SaveSubtitleStyle(x => x.SubtitleShadowOffset = Math.Clamp(value, 0, 20));
    partial void OnSubtitleAlignYChanged(string value) => SaveSubtitleStyle(x => x.SubtitleAlignY = NormalizeSubtitleAlignY(value, "bottom"));
    partial void OnSubtitleAlignXChanged(string value) => SaveSubtitleStyle(x => x.SubtitleAlignX = NormalizeSubtitleAlignX(value, "center"));
    partial void OnSubtitleMarginYChanged(int value) => SaveSubtitleStyle(x => x.SubtitleMarginY = Math.Clamp(value, 0, 500));
    partial void OnSubtitleScaleByWindowChanged(bool value) => SaveSubtitleStyle(x => x.SubtitleScaleByWindow = value);

    private void SaveSubtitleStyle(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), SettingsSection.Player);
        ApplySubtitleStyleOverride();
    }

    private void ApplySubtitleStyleOverride()
    {
        _settingsApplier.ApplySubtitleStyle(new PlayerSubtitleStyleOptions(
            SubtitleStyleOverrideEnabled,
            NormalizeMpvOption(SubtitleFont, "Candara Bold"),
            Math.Clamp(SubtitleFontSize, 1, 300),
            NormalizeSubtitleColor(SubtitleColor, "#FFFFFF"),
            NormalizeSubtitleColor(SubtitleBorderColor, "#000000"),
            NormalizeSubtitleColor(SubtitleShadowColor, "#000000"),
            Math.Clamp(SubtitleBorderSize, 0, 20),
            Math.Clamp(SubtitleShadowOffset, 0, 20),
            NormalizeSubtitleAlignY(SubtitleAlignY, "bottom"),
            NormalizeSubtitleAlignX(SubtitleAlignX, "center"),
            Math.Clamp(SubtitleMarginY, 0, 500),
            SubtitleScaleByWindow));
    }

    private static string NormalizeSubtitleColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
            trimmed = $"#{trimmed}";

        var hex = trimmed[1..];
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit)
            ? $"#{hex.ToUpperInvariant()}"
            : fallback;
    }

    private static string NormalizeSubtitleAlignX(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed is "left" or "center" or "right"
            ? trimmed
            : fallback;
    }

    private static string NormalizeSubtitleAlignY(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed is "top" or "center" or "bottom"
            ? trimmed
            : fallback;
    }
}
