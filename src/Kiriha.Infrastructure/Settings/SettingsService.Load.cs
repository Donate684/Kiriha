using System.Diagnostics;
using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Infrastructure.Settings;

public partial class SettingsService
{
    public void Load()
    {
        _saveLock.Wait();
        try
        {
            WithFileLock(LoadFiles);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load settings");
            if (_savedApp == null)
                UpdateBaselines(Current);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void LoadFiles()
    {
        var sw = Stopwatch.StartNew();
        EnsureDirectory();

        bool hasSplitFiles = HasDocument(_appSettingsPath)
            || HasDocument(_playerSettingsPath)
            || HasDocument(_torrentsSettingsPath)
            || HasDocument(_authSettingsPath)
            || HasDocument(_windowSettingsPath);

        if (hasSplitFiles)
        {
            var loaded = new AppSettings();

            var appConfig = TryLoadWithBackup(_appSettingsPath, AppSettingsJsonContext.Default.AppConfigFile);
            if (appConfig != null)
            {
                loaded.UI = appConfig.UI;
                loaded.System = appConfig.System;
                loaded.CustomLinks = appConfig.CustomLinks;
            }

            var playerConfig = TryLoadWithBackup(_playerSettingsPath, AppSettingsJsonContext.Default.PlayerConfig);
            if (playerConfig != null)
            {
                loaded.Player = playerConfig;
            }

            var torrentsConfig = TryLoadWithBackup(_torrentsSettingsPath, AppSettingsJsonContext.Default.TorrentConfig);
            if (torrentsConfig != null)
            {
                DecryptOnlineStreamingConfig(torrentsConfig);
                loaded.Torrents = torrentsConfig;
            }

            var apiConfig = TryLoadWithBackup(_authSettingsPath, AppSettingsJsonContext.Default.ApiConfig);
            if (apiConfig != null)
            {
                DecryptApiConfig(apiConfig);
                if (apiConfig.Shiki != null && apiConfig.Shiki.Mirror != apiConfig.ShikiMirror)
                {
                    Log.Information("Synchronizing Shiki token mirror with active mirror ({Mirror})", apiConfig.ShikiMirror);
                    apiConfig.Shiki.Mirror = apiConfig.ShikiMirror;
                }
                loaded.Api = apiConfig;
            }

            var windowConfig = TryLoadWithBackup(_windowSettingsPath, AppSettingsJsonContext.Default.WindowPlacement);
            if (windowConfig != null)
            {
                loaded.UI.Window = windowConfig;
            }

            SetCurrent(loaded);
            UpdateBaselines(loaded);
            Log.Information("Settings loaded from {Dir} elapsedMs={ElapsedMs}", _settingsDir, sw.ElapsedMilliseconds);
            return;
        }

        if (File.Exists(_legacySettingsPath))
        {
            Log.Information("Migrating legacy settings from {Path}", _legacySettingsPath);
            var legacy = LoadLegacySettingsFile(_legacySettingsPath);
            if (legacy != null)
            {
                SetCurrent(legacy);
                SaveFiles();
                Log.Information("Legacy settings migrated to split files in {Dir} elapsedMs={ElapsedMs}", _settingsDir, sw.ElapsedMilliseconds);
                return;
            }
        }

        Log.Information("Settings files not found, initializing defaults in {Dir}", _settingsDir);
        SetCurrent(new AppSettings());
        SaveFiles();
    }

    private static bool HasDocument(string path) => File.Exists(path) || File.Exists(GetBackupPath(path));

    private void UpdateBaselines(AppSettings settings)
    {
        var json = AppSettingsJsonContext.Default;
        _savedApp = ToJsonObject(GetAppConfig(settings), json.AppConfigFile);
        _savedPlayer = ToJsonObject(settings.Player, json.PlayerConfig);
        _savedTorrents = ToJsonObject(settings.Torrents, json.TorrentConfig);
        _savedAuth = ToJsonObject(settings.Api, json.ApiConfig);
        _savedWindow = ToJsonObject(settings.UI.Window, json.WindowPlacement);
    }

    private AppSettings? LoadLegacySettingsFile(string path)
    {
        try
        {
            var json = ReadAllTextShared(path);
            var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            if (loaded is null)
                return null;

            DecryptApiConfig(loaded.Api);
            if (loaded.Api.Shiki != null && loaded.Api.Shiki.Mirror != loaded.Api.ShikiMirror)
            {
                loaded.Api.Shiki.Mirror = loaded.Api.ShikiMirror;
            }
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load legacy settings from {Path}", path);
            return null;
        }
    }
}
