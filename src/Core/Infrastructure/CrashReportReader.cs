using System;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace Kiriha.Core.Infrastructure;

public static class CrashReportReader
{
    public static string? GetPendingCrashFile()
    {
        try
        {
            if (!Directory.Exists(CrashReporter.CrashesDir)) return null;
            var files = Directory.GetFiles(CrashReporter.CrashesDir, "crash-*.txt", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return null;
            return files
                .Select(f => new { Path = f, Time = SafeGetWriteTime(f) })
                .OrderByDescending(x => x.Time)
                .First()
                .Path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CrashReportReader: GetPendingCrashFile failed");
            return null;
        }
    }

    public static string ReadReport(string crashFilePath)
    {
        try { return File.ReadAllText(crashFilePath, Encoding.UTF8); }
        catch (Exception ex)
        {
            Log.Warning(ex, "CrashReportReader: ReadReport failed for {File}", crashFilePath);
            return $"(failed to read crash file: {ex.Message})";
        }
    }

    public static void MarkSeen(string crashFilePath)
    {
        try
        {
            if (!File.Exists(crashFilePath)) return;
            Directory.CreateDirectory(CrashReporter.SeenDir);
            var dest = Path.Combine(CrashReporter.SeenDir, Path.GetFileName(crashFilePath));
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(crashFilePath, dest);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CrashReportReader: MarkSeen failed for {File}", crashFilePath);
        }
    }

    public static string GetCrashesDir()
    {
        Directory.CreateDirectory(CrashReporter.CrashesDir);
        return CrashReporter.CrashesDir;
    }

    private static DateTime SafeGetWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception ex)
        {
            Log.Debug(ex, "CrashReportReader: GetLastWriteTimeUtc failed for {File}", path);
            return DateTime.MinValue;
        }
    }
}
