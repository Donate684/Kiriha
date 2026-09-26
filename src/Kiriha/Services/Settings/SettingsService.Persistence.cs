using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Serilog;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService
{
    public void SaveImmediate()
    {
        bool lockTaken = false;
        try
        {
            lockTaken = _saveLock.Wait(TimeSpan.FromSeconds(2));
            if (lockTaken)
            {
                InternalSaveSync();
            }
            else
            {
                Log.Warning("SettingsService: SaveImmediate timed out waiting for save lock");
            }
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (lockTaken)
            {
                try { _saveLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    public async Task SaveAsync()
    {
        bool lockTaken = false;
        try
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (CanSkipSave())
                return;

            await Task.Run(InternalSaveSync).ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
            {
                try { _saveLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private void InternalSaveSync()
    {
        if (CanSkipSave())
            return;

        EnsureDirectory();

        AppSettings snapshot;
        SettingsVersions versions;
        lock (_stateLock)
        {
            snapshot = CloneSettings(_current);
            versions = GetVersions();
        }

        bool forceAll = versions.Ui == 0 && versions.System == 0 && versions.Player == 0
            && versions.Torrents == 0 && versions.Api == 0 && versions.CustomLinks == 0 && versions.Window == 0;

        // 1. app.json
        if (forceAll || versions.Ui != 0 || versions.System != 0 || versions.CustomLinks != 0 || !File.Exists(_appSettingsPath))
        {
            var appJson = SerializeAppConfig(snapshot);
            if (!string.Equals(appJson, _lastSavedAppJson, StringComparison.Ordinal) || !File.Exists(_appSettingsPath))
            {
                AtomicWrite(_appSettingsPath, appJson);
                _lastSavedAppJson = appJson;
            }
        }

        // 2. player.json
        if (forceAll || versions.Player != 0 || !File.Exists(_playerSettingsPath))
        {
            var playerJson = JsonSerializer.Serialize(snapshot.Player, AppSettingsJsonContext.Default.PlayerConfig);
            if (!string.Equals(playerJson, _lastSavedPlayerJson, StringComparison.Ordinal) || !File.Exists(_playerSettingsPath))
            {
                AtomicWrite(_playerSettingsPath, playerJson);
                _lastSavedPlayerJson = playerJson;
            }
        }

        // 3. torrents.json
        if (forceAll || versions.Torrents != 0 || !File.Exists(_torrentsSettingsPath))
        {
            var torrentsJson = JsonSerializer.Serialize(snapshot.Torrents, AppSettingsJsonContext.Default.TorrentConfig);
            if (!string.Equals(torrentsJson, _lastSavedTorrentsJson, StringComparison.Ordinal) || !File.Exists(_torrentsSettingsPath))
            {
                AtomicWrite(_torrentsSettingsPath, torrentsJson);
                _lastSavedTorrentsJson = torrentsJson;
            }
        }

        // 4. auth.json
        if (forceAll || versions.Api != 0 || !File.Exists(_authSettingsPath))
        {
            var authJson = SerializeAuth(snapshot.Api);
            if (!string.Equals(authJson, _lastSavedAuthJson, StringComparison.Ordinal) || !File.Exists(_authSettingsPath))
            {
                AtomicWrite(_authSettingsPath, authJson);
                _lastSavedAuthJson = authJson;
            }
        }

        // 5. window.json
        if (forceAll || versions.Window != 0 || !File.Exists(_windowSettingsPath))
        {
            var windowJson = JsonSerializer.Serialize(snapshot.UI.Window, AppSettingsJsonContext.Default.WindowPlacement);
            if (!string.Equals(windowJson, _lastSavedWindowJson, StringComparison.Ordinal) || !File.Exists(_windowSettingsPath))
            {
                AtomicWrite(_windowSettingsPath, windowJson);
                _lastSavedWindowJson = windowJson;
            }
        }

        MarkVersionsSaved(versions);
    }

    private static string SerializeAppConfig(AppSettings settings)
    {
        var file = new AppConfigFile
        {
            UI = settings.UI,
            System = settings.System,
            CustomLinks = settings.CustomLinks
        };
        return JsonSerializer.Serialize(file, AppSettingsJsonContext.Default.AppConfigFile);
    }

    private string SerializeAuth(AppSettings.ApiConfig api)
    {
        var clone = new AppSettings.ApiConfig
        {
            ShikiMirror = api.ShikiMirror,
            Mal = api.Mal == null ? null : new MalTokens
            {
                AccessToken = api.Mal.AccessToken,
                RefreshToken = api.Mal.RefreshToken,
                ExpiresIn = api.Mal.ExpiresIn,
                CreatedAt = api.Mal.CreatedAt
            },
            Shiki = api.Shiki == null ? null : new ShikiTokens
            {
                AccessToken = api.Shiki.AccessToken,
                RefreshToken = api.Shiki.RefreshToken,
                CreatedAt = api.Shiki.CreatedAt,
                ExpiresIn = api.Shiki.ExpiresIn
            }
        };
        EncryptTokens(clone.Mal);
        EncryptTokens(clone.Shiki);
        return JsonSerializer.Serialize(clone, AppSettingsJsonContext.Default.ApiConfig);
    }

    private bool CanSkipSave()
    {
        lock (_stateLock)
        {
            return _uiVersion == 0
                && _systemVersion == 0
                && _playerVersion == 0
                && _torrentsVersion == 0
                && _apiVersion == 0
                && _customLinksVersion == 0
                && _windowVersion == 0
                && File.Exists(_appSettingsPath)
                && File.Exists(_playerSettingsPath)
                && File.Exists(_torrentsSettingsPath)
                && File.Exists(_authSettingsPath)
                && File.Exists(_windowSettingsPath);
        }
    }

    private readonly record struct SettingsVersions(
        long Ui,
        long System,
        long Player,
        long Torrents,
        long Api,
        long CustomLinks,
        long Window);

    private void EnsureDirectory()
    {
        if (!string.IsNullOrEmpty(_settingsDir) && !Directory.Exists(_settingsDir))
            Directory.CreateDirectory(_settingsDir);
    }
}
