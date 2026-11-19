using System.Runtime.InteropServices;

namespace Kiriha.Infrastructure.Tracking.ExternalDetection.Strategies;

public static unsafe partial class HandleEnumerationStrategy
{
    private const int SystemExtendedHandleInformation = 64;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    private const int STATUS_SUCCESS = 0;

    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const uint FILE_TYPE_DISK = 0x0001;

    private const uint FILE_READ_DATA = 0x0001;
    private const uint FILE_WRITE_DATA = 0x0002;
    private const uint FILE_APPEND_DATA = 0x0004;
    private const uint ACCESS_MASK_PIPE_GENERIC = 0x0012019f;
    private const uint ACCESS_MASK_PIPE_READ = 0x00120189;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandleW(
        IntPtr hFile,
        char* lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(IntPtr hFile);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelSynchronousIo(IntPtr hThread);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentThread();
}
