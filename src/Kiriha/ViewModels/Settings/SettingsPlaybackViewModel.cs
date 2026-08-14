using Kiriha.Core.Abstractions.Services;
using Kiriha.Infrastructure.Tracking.Integration;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Services.Data.Settings;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Services;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;
using Kiriha.ViewModels.Player;
using Kiriha.Views;
using Kiriha.Views.Player;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsPlaybackViewModel : ObservableObject
{
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly SystemIntegrationService _systemIntegrationService;
    private readonly IExternalMediaDetector _IExternalMediaDetector;

    [ObservableProperty] private bool _isSystemPlayer;
    [ObservableProperty] private bool _keepPlayerProcessAlive;
    [ObservableProperty] private bool _singlePlayerWindow = true;
    [ObservableProperty] private string _mpvHwdec = "auto";
    [ObservableProperty] private string _mpvVideoOutput = "gpu-next";
    [ObservableProperty] private string _mpvGpuApi = "auto";
    [ObservableProperty] private string _mpvGpuContext = "auto";

    public int EnabledPlayersCount => _settingsService.Current.System.Scrobbler.AllowedProcesses.Count;

    public SettingsPlaybackViewModel(
        Kiriha.Core.Abstractions.Services.ISettingsService settingsService, 
        SystemIntegrationService systemIntegrationService,
        IExternalMediaDetector IExternalMediaDetector)
    {
        _settingsService = settingsService;
        _systemIntegrationService = systemIntegrationService;
        _IExternalMediaDetector = IExternalMediaDetector;

        IsSystemPlayer = _systemIntegrationService.IsRegistered();
        KeepPlayerProcessAlive = _settingsService.Current.System.KeepPlayerProcessAlive;
        SinglePlayerWindow = _settingsService.Current.Player.SingleWindow;
        MpvHwdec = NormalizeMpvOption(_settingsService.Current.Player.MpvHwdec, "auto");
        MpvVideoOutput = NormalizeMpvOption(_settingsService.Current.Player.MpvVideoOutput, "gpu-next");
        MpvGpuApi = NormalizeMpvOption(_settingsService.Current.Player.MpvGpuApi, "auto");
        MpvGpuContext = NormalizeMpvOption(_settingsService.Current.Player.MpvGpuContext, "auto");
    }

    partial void OnKeepPlayerProcessAliveChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.KeepPlayerProcessAlive = value, Kiriha.Core.Abstractions.Services.SettingsSection.System);
        if (value) Kiriha.Infrastructure.Player.PlayerProcessBridge.StartResident();
        else _ = Kiriha.Infrastructure.Player.PlayerProcessBridge.StopResidentAsync();
    }

    partial void OnSinglePlayerWindowChanged(bool value) => _settingsService.Update(settings => settings.Player.SingleWindow = value, Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    partial void OnMpvHwdecChanged(string value) => SaveMpvOption(x => x.MpvHwdec = NormalizeMpvOption(value, "auto"));
    partial void OnMpvVideoOutputChanged(string value) => SaveMpvOption(x => x.MpvVideoOutput = NormalizeMpvOption(value, "gpu-next"));
    partial void OnMpvGpuApiChanged(string value) => SaveMpvOption(x => x.MpvGpuApi = NormalizeMpvOption(value, "auto"));
    partial void OnMpvGpuContextChanged(string value) => SaveMpvOption(x => x.MpvGpuContext = NormalizeMpvOption(value, "auto"));

    private void SaveMpvOption(System.Action<Kiriha.Core.Domain.Models.AppSettings.PlayerConfig> update) => _settingsService.Update(settings => update(settings.Player), Kiriha.Core.Abstractions.Services.SettingsSection.Player);

    private static string NormalizeMpvOption(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    [RelayCommand]
    private async Task ManagePlayers()
    {
        using var viewModel = new PlayerSelectionViewModel(_IExternalMediaDetector, _settingsService);
        var window = new PlayerSelectionWindow(_settingsService) { DataContext = viewModel };

        var mainWindow = (App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow != null)
        {
            await window.ShowDialog(mainWindow);
            OnPropertyChanged(nameof(EnabledPlayersCount));
        }
    }

    [RelayCommand]
    private void RegisterSystemPlayer()
    {
        _systemIntegrationService.Register();
        IsSystemPlayer = _systemIntegrationService.IsRegistered();
    }

    [RelayCommand]
    private void UnregisterSystemPlayer()
    {
        _systemIntegrationService.Unregister();
        IsSystemPlayer = _systemIntegrationService.IsRegistered();
    }
}

