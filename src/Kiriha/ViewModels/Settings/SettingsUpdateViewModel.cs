using Kiriha.Core.Domain.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Services;
using Serilog;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsUpdateViewModel : ObservableObject
{
    private readonly UpdateService _updateService;

    public SettingsUpdateViewModel(UpdateService updateService)
    {
        _updateService = updateService;
        IsUpdateAvailable = _updateService.IsUpdateAvailable;
        NewVersion = _updateService.NewVersion;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _newVersion;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    private int _updateProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    private bool _isUpdateDownloaded;

    public bool IsDownloadReady => IsUpdateAvailable && !IsUpdateDownloaded && UpdateProgress == 0;
    public bool IsDownloading => UpdateProgress > 0 && !IsUpdateDownloaded;

    [RelayCommand]
    private void OpenReleasesPage()
    {
        UIUtils.OpenUrl(AppConstants.Links.GitHubReleases);
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (_updateService.IsChecking) return;

        IsCheckingUpdates = true;
        IsUpdateAvailable = false;
        IsUpdateDownloaded = false;
        UpdateProgress = 0;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var found = await _updateService.CheckForUpdatesAsync(cts.Token);
            IsUpdateAvailable = found;
            NewVersion = _updateService.NewVersion;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CheckForUpdates command failed");
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (!IsUpdateAvailable || IsUpdateDownloaded || _updateService.IsDownloading) return;

        UpdateProgress = 1;
        try
        {
            using var cts = new CancellationTokenSource();
            var success = await _updateService.DownloadAndInstallAsync(p => UpdateProgress = p, cts.Token);
            if (success)
            {
                IsUpdateDownloaded = true;
            }
            else
            {
                UpdateProgress = 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DownloadUpdate command failed");
            UpdateProgress = 0;
        }
    }

    [RelayCommand]
    private void RestartAndApply()
    {
        _updateService.RestartAndApply();
    }
}
