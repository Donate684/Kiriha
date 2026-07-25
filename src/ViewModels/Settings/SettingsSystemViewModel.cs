using Kiriha.Services.Tracking.Integration;
using Kiriha.Services.Tracking.Feed;
using Kiriha.Services.Tracking.Core;
using Kiriha.Services.Data.Settings;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Services;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Core;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsSystemViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly DiscordService _discordService;

    [ObservableProperty] private bool _autoLaunch;
    [ObservableProperty] private bool _launchMinimized;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _enableScrobbler;
    [ObservableProperty] private decimal? _scrobbleDelaySeconds;
    [ObservableProperty] private bool _scrobbleNotifyOnSkip;
    [ObservableProperty] private bool _enableDiscordRPC;
    [ObservableProperty] private bool _enableBackgroundMetadataFetch;
    [ObservableProperty] private bool _enableLogging;
    [ObservableProperty] private bool _autoCheckUpdates;
    [ObservableProperty] private bool _autoDownloadUpdates;
    [ObservableProperty] private bool _notifyNewEpisodes;
    [ObservableProperty] private bool _notifyAppUpdate;
    [ObservableProperty] private decimal? _newEpisodeNotificationDelayMinutes;

    public SettingsSystemViewModel(SettingsService settingsService, DiscordService discordService)
    {
        _settingsService = settingsService;
        _discordService = discordService;

        AutoLaunch = _settingsService.Current.System.AutoLaunch;
        LaunchMinimized = _settingsService.Current.System.LaunchMinimized;
        CloseToTray = _settingsService.Current.System.CloseToTray;
        MinimizeToTray = _settingsService.Current.System.MinimizeToTray;
        EnableScrobbler = _settingsService.Current.System.Scrobbler.Enabled;
        ScrobbleDelaySeconds = _settingsService.Current.System.Scrobbler.DelaySeconds;
        ScrobbleNotifyOnSkip = _settingsService.Current.System.Scrobbler.NotifyOnSkippedEpisode;
        EnableDiscordRPC = _settingsService.Current.System.EnableDiscordRPC;
        EnableBackgroundMetadataFetch = _settingsService.Current.System.EnableBackgroundMetadataFetch;
        EnableLogging = _settingsService.Current.System.EnableLogging;
        AutoCheckUpdates = _settingsService.Current.System.AutoCheckUpdates;
        AutoDownloadUpdates = _settingsService.Current.System.AutoDownloadUpdates;
        NotifyNewEpisodes = _settingsService.Current.System.NotifyNewEpisodes;
        NotifyAppUpdate = _settingsService.Current.System.NotifyAppUpdate;
        NewEpisodeNotificationDelayMinutes = _settingsService.Current.System.NewEpisodeNotificationDelayMinutes;
    }

    partial void OnAutoLaunchChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.AutoLaunch = value, SettingsSection.System);
        if (value) StartupService.EnableStartup(LaunchMinimized);
        else StartupService.DisableStartup();
    }

    partial void OnLaunchMinimizedChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.LaunchMinimized = value, SettingsSection.System);
        if (AutoLaunch) StartupService.EnableStartup(value);
    }

    partial void OnCloseToTrayChanged(bool value) => _settingsService.Update(settings => settings.System.CloseToTray = value, SettingsSection.System);
    partial void OnMinimizeToTrayChanged(bool value) => _settingsService.Update(settings => settings.System.MinimizeToTray = value, SettingsSection.System);
    partial void OnEnableBackgroundMetadataFetchChanged(bool value) => _settingsService.Update(settings => settings.System.EnableBackgroundMetadataFetch = value, SettingsSection.System);
    partial void OnEnableLoggingChanged(bool value) => _settingsService.Update(settings => settings.System.EnableLogging = value, SettingsSection.System);
    partial void OnEnableScrobblerChanged(bool value) => _settingsService.Update(settings => settings.System.Scrobbler.Enabled = value, SettingsSection.System);
    partial void OnScrobbleDelaySecondsChanged(decimal? value)
    {
        if (value.HasValue) _settingsService.Update(settings => settings.System.Scrobbler.DelaySeconds = (int)value.Value, SettingsSection.System);
    }
    partial void OnScrobbleNotifyOnSkipChanged(bool value) => _settingsService.Update(settings => settings.System.Scrobbler.NotifyOnSkippedEpisode = value, SettingsSection.System);

    partial void OnEnableDiscordRPCChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.EnableDiscordRPC = value, SettingsSection.System);
        _discordService.UpdateStatus(value);
    }

    partial void OnAutoCheckUpdatesChanged(bool value) => _settingsService.Update(settings => settings.System.AutoCheckUpdates = value, SettingsSection.System);
    partial void OnAutoDownloadUpdatesChanged(bool value) => _settingsService.Update(settings => settings.System.AutoDownloadUpdates = value, SettingsSection.System);
    partial void OnNotifyNewEpisodesChanged(bool value) => _settingsService.Update(settings => settings.System.NotifyNewEpisodes = value, SettingsSection.System);
    partial void OnNotifyAppUpdateChanged(bool value) => _settingsService.Update(settings => settings.System.NotifyAppUpdate = value, SettingsSection.System);
    partial void OnNewEpisodeNotificationDelayMinutesChanged(decimal? value)
    {
        if (value.HasValue)
        {
            var minutes = (int)Math.Max(0, value.Value);
            _settingsService.Update(settings => settings.System.NewEpisodeNotificationDelayMinutes = minutes, SettingsSection.System);
        }
    }
}
