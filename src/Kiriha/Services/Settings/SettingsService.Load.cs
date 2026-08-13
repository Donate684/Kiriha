using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Kiriha.Models;
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


}
