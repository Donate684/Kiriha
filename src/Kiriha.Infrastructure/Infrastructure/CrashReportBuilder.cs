using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Kiriha.Infrastructure.Platform;
using Serilog;

namespace Kiriha.Infrastructure;

public static class CrashReportBuilder
{
    private const int LogTailLines = 500;

    public static string BuildReport(Exception? exception, string source)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Kiriha crash report ===");
        sb.AppendLine($"Time      : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Source    : {source}");
        sb.AppendLine($"Version   : {GetAppVersion()}");
        sb.AppendLine($"OS        : {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})");
        sb.AppendLine($"Runtime   : {Environment.Version} / {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"CPUs      : {Environment.ProcessorCount}");
        sb.AppendLine($"WorkSet   : {Environment.WorkingSet / (1024 * 1024)} MB");
        sb.AppendLine();

        sb.AppendLine("=== Exception ===");
        if (exception != null)
        {
            sb.AppendLine(exception.ToString());
        }
        else
        {
            sb.AppendLine("(no exception object captured)");
        }
        sb.AppendLine();

        sb.AppendLine($"=== Last {LogTailLines} log lines ===");
        sb.AppendLine(ReadLogTail(LogTailLines));

        return sb.ToString();
    }

    private static string GetAppVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info)) return info;
            return asm.GetName().Version?.ToString() ?? "unknown";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "CrashReportBuilder: GetAppVersion failed");
            return "unknown";
        }
    }

    private static string ReadLogTail(int maxLines)
    {
        try
        {
            var logsDir = PathHelper.GetLogsPath();
            if (!Directory.Exists(logsDir)) return "(no logs directory)";

            var latest = Directory.GetFiles(logsDir, "kiriha-*.txt", SearchOption.TopDirectoryOnly)
                                  .OrderByDescending(f => f)
                                  .FirstOrDefault();
            if (latest == null) return "(no log file found)";

            var queue = new Queue<string>(maxLines);
            using var fs = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (queue.Count == maxLines) queue.Dequeue();
                queue.Enqueue(line);
            }
            return string.Join(Environment.NewLine, queue);
        }
        catch (Exception ex)
        {
            return $"(failed to read log tail: {ex.Message})";
        }
    }
}
