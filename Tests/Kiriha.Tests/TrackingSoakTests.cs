using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Tracking.Anisthesia;
using Kiriha.Core.Abstractions.Services;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class TrackingSoakTests
{
    [Fact]
    public async Task DetectionManager_DetectAsync_DoesNotLeakMemoryOrHandles()
    {
        if (!OperatingSystem.IsWindows())
            return; // Only works on Windows

        // Arrange
        var mockSettings = new Mock<ISettingsService>();
        
        // Add a dummy player so it scans but doesn't do deep parsing
        var dummyPlayer = new AnisthesiaPlayer 
        { 
            Name = "DummyPlayer",
            Executables = new List<string> { "non_existent_process_123" }
        };

        var manager = new DetectionManager(new List<AnisthesiaPlayer> { dummyPlayer }, mockSettings.Object);

        // Warmup (to jits methods and load static caches)
        for (int i = 0; i < 10; i++)
        {
            await manager.DetectAsync();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialMemory = GC.GetTotalMemory(true);
        var initialHandles = Process.GetCurrentProcess().HandleCount;

        // Act - 1000 iterations (equivalent to ~8 hours of tracking at 1 scan / 30s)
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            await manager.DetectAsync();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);
        var finalHandles = Process.GetCurrentProcess().HandleCount;

        // Assert
        long memoryDiff = finalMemory - initialMemory;
        int handleDiff = finalHandles - initialHandles;

        // Allow up to 2MB of memory difference for static caches growing or normal fragmentation
        Assert.True(memoryDiff < 2 * 1024 * 1024, $"Memory leak detected! Increased by {memoryDiff / 1024.0:F2} KB");
        
        // Allow up to 50 handles difference for thread pool / temp OS handles
        Assert.True(handleDiff < 50, $"Handle leak detected! Increased by {handleDiff} handles");
    }
}
