using System;
using System.IO;
using System.Text;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Serilog;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService
{
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
