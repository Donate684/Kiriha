using Kiriha.Services.Data.Settings;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
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
                Log.Warning("SettingsService: SaveImmediate timed out waiting for save lock, skipping save to avoid deadlock.");
            }
        }
        catch (ObjectDisposedException)
        {
            // Ignore if disposed
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

            await Task.Run(() =>
            {
                EnsureDirectory();
                var save = PrepareJsonForSave();
                var json = EncryptForSave(save.Settings);

                if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal))
                {
                    MarkVersionsSaved(save.Versions);
                    Log.Debug("Settings save skipped (async): content unchanged ({Path})", _settingsPath);
                    return;
                }

                AtomicWrite(_settingsPath, json);
                _lastSavedJson = json;
                MarkVersionsSaved(save.Versions);
                Log.Debug("Settings saved (async) to {Path}", _settingsPath);
            }).ConfigureAwait(false);
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
        var save = PrepareJsonForSave();
        var json = EncryptForSave(save.Settings);

        if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal))
        {
            MarkVersionsSaved(save.Versions);
            Log.Debug("Settings save skipped: content unchanged ({Path})", _settingsPath);
            return;
        }

        AtomicWrite(_settingsPath, json);
        _lastSavedJson = json;
        MarkVersionsSaved(save.Versions);
        Log.Information("Settings saved to {Path}", _settingsPath);
    }

    /// <summary>
    /// Writes settings to a temp sibling file, then atomically replaces the destination.
    /// Prevents corrupted/half-written settings (and therefore token loss) if the process
    /// is killed mid-write or the disk fills up.
    /// </summary>
    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        // File.Replace requires the destination to exist; fall back to Move on first save.
        if (File.Exists(path)) File.Replace(tmp, path, CanBackupCurrentSettings(path) ? GetBackupPath(path) : null);
        else File.Move(tmp, path);
    }

    private PendingSettingsSave PrepareJsonForSave()
    {
        AppSettings snapshot;
        SettingsVersions versions;
        lock (_stateLock)
        {
            snapshot = CloneSettings(_current);
            versions = GetVersions();
        }

        var merged = TryLoadSettingsFromDisk() ?? snapshot;

        if (versions.Ui != 0) merged.UI = snapshot.UI;
        if (versions.System != 0) merged.System = snapshot.System;
        if (versions.Player != 0) merged.Player = snapshot.Player;
        if (versions.Torrents != 0) merged.Torrents = snapshot.Torrents;
        if (versions.Api != 0) merged.Api = snapshot.Api;
        if (versions.CustomLinks != 0) merged.CustomLinks = snapshot.CustomLinks;

        return new PendingSettingsSave(merged, versions);
    }

    private string EncryptForSave(AppSettings settings)
    {
        var clone = CloneSettings(settings);
        EncryptTokens(clone.Api.Mal);
        EncryptTokens(clone.Api.Shiki);
        return JsonSerializer.Serialize(clone, AppSettingsJsonContext.Default.AppSettings);
    }



    private bool CanSkipSave()
    {
        if (!File.Exists(_settingsPath))
            return false;

        lock (_stateLock)
        {
            return _uiVersion == 0
                && _systemVersion == 0
                && _playerVersion == 0
                && _torrentsVersion == 0
                && _apiVersion == 0
                && _customLinksVersion == 0;
        }
    }

    private readonly record struct PendingSettingsSave(AppSettings Settings, SettingsVersions Versions);

    private readonly record struct SettingsVersions(
        long Ui,
        long System,
        long Player,
        long Torrents,
        long Api,
        long CustomLinks);

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }
}
