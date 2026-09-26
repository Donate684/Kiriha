using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Serilog;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService
{
    private T? TryLoadWithBackup<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (File.Exists(path))
        {
            try
            {
                var json = ReadAllTextShared(path);
                var obj = JsonSerializer.Deserialize(json, typeInfo);
                if (obj is not null)
                    return obj;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Primary settings file {Path} is corrupted, attempting to restore from backup", path);
            }
        }

        var backupPath = GetBackupPath(path);
        if (File.Exists(backupPath))
        {
            try
            {
                var json = ReadAllTextShared(backupPath);
                var obj = JsonSerializer.Deserialize(json, typeInfo);
                if (obj is not null)
                {
                    Log.Information("Successfully restored {Path} from backup {BackupPath}", path, backupPath);
                    return obj;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restore settings from backup {BackupPath}", backupPath);
            }
        }

        return null;
    }

    private static string GetBackupPath(string path) => path + ".bak";

    private static void AtomicWrite(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
        {
            File.Replace(tmp, path, CanBackupCurrentSettings(path) ? GetBackupPath(path) : null);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

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
