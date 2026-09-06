using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Kiriha.Infrastructure.Tracking.Anisthesia;

/// <summary>
/// Low-allocation Win32 process enumerator using CreateToolhelp32Snapshot.
/// Avoids the heavy managed Process.GetProcesses() overhead (which instantiates
/// 300+ Process objects, modules, and native handle queries every tick).
/// </summary>
public static class WindowsProcessSnapshot
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static unsafe bool TryEnumerateProcesses(List<(uint Pid, string ProcessName)> results)
    {
        if (!OperatingSystem.IsWindows()) return false;

        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero) return false;

        results.Clear();

        try
        {
            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)sizeof(PROCESSENTRY32);

            if (!Process32First(snapshot, ref entry)) return false;

            do
            {
                char* ptr = entry.szExeFile;
                var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
                if (span.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    span = span[..^4];
                }

                if (!span.IsEmpty)
                {
                    results.Add((entry.th32ProcessID, span.ToString()));
                }
            } while (Process32Next(snapshot, ref entry));

            return true;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }
}
