using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Kiriha.Infrastructure.Tracking.Anisthesia;
using Xunit;

namespace Kiriha.Tests.Services;

public class ProcessSnapshotTests
{
    [Fact]
    public void WindowsProcessSnapshot_FindsCurrentProcess()
    {
        if (!OperatingSystem.IsWindows()) return;

        var buffer = new List<(uint Pid, string ProcessName)>();
        bool success = WindowsProcessSnapshot.TryEnumerateProcesses(buffer);

        Assert.True(success);
        Assert.NotEmpty(buffer);

        uint currentPid = (uint)Environment.ProcessId;
        var currentProc = buffer.FirstOrDefault(p => p.Pid == currentPid);

        Assert.NotEqual(default, currentProc);
        Assert.False(string.IsNullOrWhiteSpace(currentProc.ProcessName));
        Assert.False(currentProc.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WindowsProcessSnapshot_ClearsAndReusesBuffer()
    {
        if (!OperatingSystem.IsWindows()) return;

        var buffer = new List<(uint Pid, string ProcessName)> { (999999, "FakeProcess") };
        bool success = WindowsProcessSnapshot.TryEnumerateProcesses(buffer);

        Assert.True(success);
        Assert.DoesNotContain(buffer, p => p.Pid == 999999);
    }
}
