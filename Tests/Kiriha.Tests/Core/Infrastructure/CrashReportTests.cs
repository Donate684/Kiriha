using System;
using System.IO;
using System.Linq;
using System.Text;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Xunit;

namespace Kiriha.Tests.Core.Infrastructure;

public class CrashReportTests : IDisposable
{
    public CrashReportTests()
    {
        Cleanup();
    }

    public void Dispose()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        var dir = CrashReportReader.GetCrashesDir();
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
        }
        
        if (Directory.Exists(CrashReporter.SeenDir))
        {
            foreach (var f in Directory.GetFiles(CrashReporter.SeenDir)) File.Delete(f);
        }
    }

    [Fact]
    public void GetPendingCrashFile_ReturnsNewestCrashFile()
    {
        var dir = CrashReportReader.GetCrashesDir();
        var older = Path.Combine(dir, "crash-older.txt");
        var newer = Path.Combine(dir, "crash-newer.txt");

        File.WriteAllText(older, "old");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-5));

        File.WriteAllText(newer, "new");
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-1));

        var pending = CrashReportReader.GetPendingCrashFile();
        Assert.Equal(newer, pending);
    }

    [Fact]
    public void ReadReport_ReadsFileContents()
    {
        var dir = CrashReportReader.GetCrashesDir();
        var file = Path.Combine(dir, "crash-test.txt");
        File.WriteAllText(file, "test content");

        var content = CrashReportReader.ReadReport(file);
        Assert.Equal("test content", content);
    }

    [Fact]
    public void MarkSeen_MovesFileToSeenDir()
    {
        var dir = CrashReportReader.GetCrashesDir();
        var file = Path.Combine(dir, "crash-toseen.txt");
        File.WriteAllText(file, "test");

        CrashReportReader.MarkSeen(file);

        Assert.False(File.Exists(file));
        
        var seenDir = CrashReporter.SeenDir;
        var dest = Path.Combine(seenDir, "crash-toseen.txt");
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void BuildReport_IncludesExceptionAndSource()
    {
        var ex = new InvalidOperationException("Test exception");
        var report = CrashReportBuilder.BuildReport(ex, "Test Source");

        Assert.Contains("=== Kiriha crash report ===", report);
        Assert.Contains("Test exception", report);
        Assert.Contains("Test Source", report);
    }
}
