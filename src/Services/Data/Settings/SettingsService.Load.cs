using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Kiriha.Models;
using Serilog;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService
{
    public void Load()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (!File.Exists(_settingsPath))
            {
                Log.Information("Settings file not found, creating new one");
                SaveImmediate();
                return;
            }

            var loaded = LoadSettingsFile(_settingsPath)
                ?? throw new JsonException("Settings file contained null JSON");
            SetCurrent(loaded);

            Log.Information("Settings loaded from {Path} elapsedMs={ElapsedMs}", _settingsPath, sw.ElapsedMilliseconds);
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Error loading settings; file is temporarily unavailable, fallback will not be saved automatically");
            SetCurrent(new AppSettings());
            Log.Information("Settings fallback initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            var backup = TryLoadBackupSettings(ex);
            if (backup != null)
            {
                SetCurrent(backup);
                MarkAllSectionsChanged();
                Log.Information("Settings restored from backup elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
                return;
            }

            Log.Error(ex, "Error loading settings");
            SetCurrent(new AppSettings());
            MarkAllSectionsChanged();
            Log.Information("Settings fallback initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
        }
    }

    private AppSettings? TryLoadSettingsFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;

            return LoadSettingsFile(_settingsPath) ?? TryLoadBackupSettings();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Settings merge: failed to read current settings from disk");
            return TryLoadBackupSettings();
        }
    }

    private AppSettings? TryLoadBackupSettings(Exception? primaryException = null)
    {
        var backupPath = GetBackupPath(_settingsPath);
        if (!File.Exists(backupPath))
            return null;

        try
        {
            var backup = LoadSettingsFile(backupPath);
            if (backup == null)
                return null;

            if (primaryException != null)
                Log.Warning(primaryException, "Settings load failed; restored from backup {BackupPath}", backupPath);
            else
                Log.Warning("Settings merge: using backup settings from {BackupPath}", backupPath);

            return backup;
        }
        catch (Exception backupException)
        {
            if (primaryException != null)
                Log.Error(backupException, "Error loading settings backup after primary load failed");
            else
                Log.Warning(backupException, "Settings merge: failed to read backup settings");

            return null;
        }
    }

    private AppSettings? LoadSettingsFile(string path)
    {
        var json = ReadAllTextShared(path);
        var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        if (loaded == null)
            return null;

        DecryptTokens(loaded.Api.Mal, loaded.Api);
        DecryptTokens(loaded.Api.Shiki, loaded.Api);
        return loaded;
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string GetBackupPath(string path) => path + ".bak";

    private static bool CanBackupCurrentSettings(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                return false;

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            int ch;
            while ((ch = reader.Read()) != -1)
            {
                if (!char.IsWhiteSpace((char)ch))
                    return ch == '{';
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
