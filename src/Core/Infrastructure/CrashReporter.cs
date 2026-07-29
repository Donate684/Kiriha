using System;
using System.IO;
using System.Text;
using Kiriha.Core.Platform;
using Serilog;

namespace Kiriha.Core.Infrastructure;

public static class CrashReporter
{
    public const string CrashesSubDir = "crashes";
    public const string SeenSubDir = "seen";

    public static string CrashesDir => Path.Combine(PathHelper.GetLogsPath(), CrashesSubDir);
    public static string SeenDir => Path.Combine(CrashesDir, SeenSubDir);

    public static void WriteCrash(Exception? exception, string source)
    {
        try
        {
            Directory.CreateDirectory(CrashesDir);
            var fileName = $"crash-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffZ", System.Globalization.CultureInfo.InvariantCulture)}.txt";
            var filePath = Path.Combine(CrashesDir, fileName);
            File.WriteAllText(filePath, CrashReportBuilder.BuildReport(exception, source), Encoding.UTF8);
            Log.Information("CrashReporter: Wrote crash snapshot {File}", filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CrashReporter: Failed to write crash snapshot");
        }
    }
}
