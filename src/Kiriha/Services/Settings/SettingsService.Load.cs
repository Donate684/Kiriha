using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService
{
    public void Load()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            EnsureDirectory();

            bool hasSplitFiles = File.Exists(_appSettingsPath)
                || File.Exists(_playerSettingsPath)
                || File.Exists(_torrentsSettingsPath)
                || File.Exists(_authSettingsPath)
                || File.Exists(_windowSettingsPath);

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
                    loaded.Torrents = torrentsConfig;
                }

                var apiConfig = TryLoadWithBackup(_authSettingsPath, AppSettingsJsonContext.Default.ApiConfig);
                if (apiConfig != null)
                {
                    DecryptTokens(apiConfig.Mal, apiConfig);
                    DecryptTokens(apiConfig.Shiki, apiConfig);
                    loaded.Api = apiConfig;
                }

                var windowConfig = TryLoadWithBackup(_windowSettingsPath, AppSettingsJsonContext.Default.WindowPlacement);
                if (windowConfig != null)
                {
                    loaded.UI.Window = windowConfig;
                }

                SetCurrent(loaded);
                UpdateCachedSavedJson(loaded);
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
                    SaveImmediate();
                    Log.Information("Legacy settings migrated to split files in {Dir} elapsedMs={ElapsedMs}", _settingsDir, sw.ElapsedMilliseconds);
                    return;
                }
            }

            Log.Information("Settings files not found, initializing defaults in {Dir}", _settingsDir);
            SetCurrent(new AppSettings());
            SaveImmediate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading settings, fallback to defaults");
            SetCurrent(new AppSettings());
            MarkAllSectionsChanged();
        }
    }

    private void UpdateCachedSavedJson(AppSettings settings)
    {
        _lastSavedAppJson = SerializeAppConfig(settings);
        _lastSavedPlayerJson = JsonSerializer.Serialize(settings.Player, AppSettingsJsonContext.Default.PlayerConfig);
        _lastSavedTorrentsJson = JsonSerializer.Serialize(settings.Torrents, AppSettingsJsonContext.Default.TorrentConfig);
        _lastSavedAuthJson = SerializeAuth(settings.Api);
        _lastSavedWindowJson = JsonSerializer.Serialize(settings.UI.Window, AppSettingsJsonContext.Default.WindowPlacement);
    }

    private AppSettings? LoadLegacySettingsFile(string path)
    {
        try
        {
            var json = ReadAllTextShared(path);
            var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            if (loaded is null)
                return null;

            DecryptTokens(loaded.Api.Mal, loaded.Api);
            DecryptTokens(loaded.Api.Shiki, loaded.Api);
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load legacy settings from {Path}", path);
            return null;
        }
    }
}
