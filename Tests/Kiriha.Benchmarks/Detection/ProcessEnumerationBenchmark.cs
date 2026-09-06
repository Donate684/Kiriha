using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Kiriha.Infrastructure.Tracking.Anisthesia;

namespace Kiriha.Benchmarks.Detection;

[MemoryDiagnoser]
[ShortRunJob]
public class ProcessEnumerationBenchmark
{
    private readonly List<(uint Pid, string ProcessName)> _reusableBuffer = new(512);

    [Benchmark(Baseline = true)]
    public int ManagedProcess_GetProcesses()
    {
        // Legacy implementation: Process.GetProcesses() creating ~300 managed Process objects and native handles
        var processes = Process.GetProcesses();
        int count = 0;
        try
        {
            for (int i = 0; i < processes.Length; i++)
            {
                var p = processes[i];
                if (!string.IsNullOrEmpty(p.ProcessName))
                {
                    count++;
                }
            }
        }
        finally
        {
            for (int i = 0; i < processes.Length; i++)
            {
                processes[i].Dispose();
            }
        }

        return count;
    }

    [Benchmark]
    public int Toolhelp32_ReusedBuffer()
    {
        // Optimized implementation: Win32 Toolhelp32 snapshot reusing List buffer
        if (WindowsProcessSnapshot.TryEnumerateProcesses(_reusableBuffer))
        {
            return _reusableBuffer.Count;
        }

        return 0;
    }
}
