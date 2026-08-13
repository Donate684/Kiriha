using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Settings;
using Kiriha.Core.Constants;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services;
using Kiriha.Services.Data;
using Kiriha.ViewModels.AnimeList;
using CommunityToolkit.Mvvm.Input;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsUiViewModel : ObservableObject
{
    private readonly Kiriha.Core.Services.ISettingsService _settingsService;
    private readonly AnimeListViewModel _animeListViewModel;
    private readonly LocalizationService _localizationService;

    public record ThemeOption(string Name, ThemeType Value);
    public record LanguageOption(string Name, string Code);

    public List<ThemeOption> AvailableThemes => new()
    {
        new ThemeOption(UIUtils.GetLoc("settings.theme.default"), ThemeType.System),
        new ThemeOption(UIUtils.GetLoc("settings.theme.light"), ThemeType.Light),
        new ThemeOption(UIUtils.GetLoc("settings.theme.dark"), ThemeType.Dark)
    };

    [ObservableProperty] private ThemeOption _selectedTheme;
    [ObservableProperty] private bool _useRussianTitles;
    [ObservableProperty] private bool _useRussianDescriptions;
    [ObservableProperty] private bool _showAiringInfo;
    [ObservableProperty] private bool _enableMica;
    [ObservableProperty] private double _uiScale;
    [ObservableProperty] private Avalonia.Media.Color? _customAccentColor;
    [ObservableProperty] private bool _enableCustomAccentColor;

    public List<double> AvailableUiScales { get; } = new() { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 };
    public bool IsMicaSupported => Kiriha.Core.Platform.Platform.IsMicaSupported;

    public List<LanguageOption> AvailableLanguages { get; } = new()
    {
        new LanguageOption(AppConstants.Languages.EnName, AppConstants.Languages.En),
        new LanguageOption(AppConstants.Languages.RuName, AppConstants.Languages.Ru)
    };

    [ObservableProperty] private LanguageOption? _selectedLanguage;

    public SettingsUiViewModel(
        Kiriha.Core.Services.ISettingsService settingsService,
        AnimeListViewModel animeListViewModel,
        LocalizationService localizationService)
    {
        _settingsService = settingsService;
        _animeListViewModel = animeListViewModel;
        _localizationService = localizationService;

        _selectedLanguage = AvailableLanguages.FirstOrDefault(x => x.Code == _settingsService.Current.UI.LanguageCode) ?? AvailableLanguages[0];
        _selectedTheme = AvailableThemes.FirstOrDefault(x => x.Value == _settingsService.Current.UI.Theme) ?? AvailableThemes[0];
        UseRussianTitles = _settingsService.Current.UI.UseRussianTitles;
        UseRussianDescriptions = _settingsService.Current.UI.UseRussianDescriptions;
        ShowAiringInfo = _settingsService.Current.UI.ShowAiringInfo;
        EnableMica = _settingsService.Current.UI.EnableMica;
        UiScale = _settingsService.Current.UI.UiScale;

        if (Avalonia.Media.Color.TryParse(_settingsService.Current.UI.CustomAccentColor, out var parsedColor))
        {
            _customAccentColor = parsedColor;
            _enableCustomAccentColor = true;
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (Application.Current == null || value == null) return;
        _settingsService.Update(settings => settings.UI.Theme = value.Value, Kiriha.Core.Services.SettingsSection.UI);
        Application.Current.RequestedThemeVariant = value.Value switch
        {
            ThemeType.Light => ThemeVariant.Light,
            ThemeType.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    partial void OnCustomAccentColorChanged(Avalonia.Media.Color? value)
    {
        if (!EnableCustomAccentColor) return;

        var hex = value.HasValue ? $"#{value.Value.A:X2}{value.Value.R:X2}{value.Value.G:X2}{value.Value.B:X2}" : string.Empty;
        _settingsService.Update(settings => settings.UI.CustomAccentColor = hex, Kiriha.Core.Services.SettingsSection.UI);
        App.ApplyCustomAccentColor(hex);
    }

    partial void OnEnableCustomAccentColorChanged(bool value)
    {
        if (value)
        {
            if (!CustomAccentColor.HasValue || CustomAccentColor.Value == Avalonia.Media.Colors.White)
                CustomAccentColor = Avalonia.Media.Color.Parse("#8A2BE2");
            else
            {
                var hex = $"#{CustomAccentColor.Value.A:X2}{CustomAccentColor.Value.R:X2}{CustomAccentColor.Value.G:X2}{CustomAccentColor.Value.B:X2}";
                _settingsService.Update(settings => settings.UI.CustomAccentColor = hex, Kiriha.Core.Services.SettingsSection.UI);
                App.ApplyCustomAccentColor(hex);
            }
        }
        else
        {
            _settingsService.Update(settings => settings.UI.CustomAccentColor = string.Empty, Kiriha.Core.Services.SettingsSection.UI);
            App.ApplyCustomAccentColor(string.Empty);
            CustomAccentColor = null;
        }
    }

    partial void OnUseRussianTitlesChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.UseRussianTitles = value, Kiriha.Core.Services.SettingsSection.UI);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnUseRussianDescriptionsChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.UseRussianDescriptions = value, Kiriha.Core.Services.SettingsSection.UI);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnUiScaleChanged(double value)
    {
        _settingsService.Update(settings => settings.UI.UiScale = value, Kiriha.Core.Services.SettingsSection.UI);
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (window is Views.KirihaWindowBase kb) kb.ApplyUiScale(value);
            }
        }
    }

    partial void OnShowAiringInfoChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.ShowAiringInfo = value, Kiriha.Core.Services.SettingsSection.UI);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value != null && value.Code != _settingsService.Read(settings => settings.UI.LanguageCode))
        {
            _settingsService.Update(settings => settings.UI.LanguageCode = value.Code, Kiriha.Core.Services.SettingsSection.UI);
            _localizationService.LoadLanguage(value.Code);
            OnPropertyChanged(nameof(AvailableThemes));
            var theme = _settingsService.Read(settings => settings.UI.Theme);
            SelectedTheme = AvailableThemes.FirstOrDefault(x => x.Value == theme) ?? AvailableThemes[0];
            _animeListViewModel.RefreshLocalization();
            if (Application.Current is App app) app.UpdateTrayMenu();
        }
    }

    partial void OnEnableMicaChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.EnableMica = value, Kiriha.Core.Services.SettingsSection.UI);
        if (Application.Current is App app && app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (window is Views.MainWindow main) main.ApplyMica();
                else if (window is Views.AnimeDetailsWindow details) details.ApplyMica();
                else if (window is Views.Player.PlayerSelectionWindow playerSelection) playerSelection.ApplyMica();
            }
        }
    }
}
