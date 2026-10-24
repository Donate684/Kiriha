using System.Runtime.InteropServices;

namespace Kiriha.Infrastructure.Tracking.Anisthesia;

/// <summary>
/// Low-allocation Win32 process enumerator using CreateToolhelp32Snapshot.
/// Avoids the heavy managed Process.GetProcesses() overhead (which instantiates
/// 300+ Process objects, modules, and native handle queries every tick).
/// </summary>
public static unsafe partial class WindowsProcessSnapshot
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(IntPtr hSnapshot, PROCESSENTRY32* lppe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(IntPtr hSnapshot, PROCESSENTRY32* lppe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    public static bool TryEnumerateProcesses(List<(uint Pid, string ProcessName)> results)
    {
        if (!OperatingSystem.IsWindows()) return false;

        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero) return false;

        results.Clear();

        try
        {
            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)sizeof(PROCESSENTRY32);

            if (!Process32FirstW(snapshot, &entry)) return false;

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
            } while (Process32NextW(snapshot, &entry));

            return true;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }
}
