using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Tracking.ExternalDetection;
using Serilog;

namespace Kiriha.Infrastructure.Tracking.ExternalDetection.Strategies;

public static unsafe partial class HandleEnumerationStrategy
{
    private static readonly FrozenSet<string> _videoExtensions = FrozenSet.ToFrozenSet(
        [".mkv", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".ogm", ".webm", ".ts", ".m4v", ".m2ts"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _videoExtensionsLookup =
        _videoExtensions.GetAlternateLookup<ReadOnlySpan<char>>();

    private const int SystemExtendedHandleInformation = 64;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    private const int STATUS_SUCCESS = 0;

    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const uint FILE_TYPE_DISK = 0x0001;

    private static int s_lastBufferSize = 4 * 1024 * 1024;
    private static volatile int s_fileObjectTypeIndex = -1;

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

    private static string FormatNormalizedPath(char* pathBuffer, uint pathLen)
    {
        if (pathLen >= 4 && pathBuffer[0] == '\\' && pathBuffer[1] == '\\' && pathBuffer[2] == '?' && pathBuffer[3] == '\\')
        {
            if (pathLen >= 8 && (pathBuffer[4] == 'U' || pathBuffer[4] == 'u') &&
                                (pathBuffer[5] == 'N' || pathBuffer[5] == 'n') &&
                                (pathBuffer[6] == 'C' || pathBuffer[6] == 'c') &&
                                pathBuffer[7] == '\\')
            {
                return "\\\\" + new string(pathBuffer, 8, (int)pathLen - 8);
            }

            return new string(pathBuffer, 4, (int)pathLen - 4);
        }

        return new string(pathBuffer, 0, (int)pathLen);
    }

    private static IntPtr QuerySystemHandleTable()
    {
        int bufferSize = Volatile.Read(ref s_lastBufferSize);
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        int status;

        while ((status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out int returnLength)) == STATUS_INFO_LENGTH_MISMATCH)
        {
            bufferSize = Math.Max(returnLength + 65536, bufferSize * 2);
            Volatile.Write(ref s_lastBufferSize, bufferSize);
            Marshal.FreeHGlobal(buffer);
            buffer = Marshal.AllocHGlobal(bufferSize);
        }

        if (status != STATUS_SUCCESS)
        {
            Marshal.FreeHGlobal(buffer);
            return IntPtr.Zero;
        }

        return buffer;
    }

    public static string? TryGetFirstOpenVideoFile(uint pid)
    {
        if (!OperatingSystem.IsWindows() || pid == 0) return null;

        IntPtr processHandle = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
        if (processHandle == IntPtr.Zero) return null;

        try
        {
            IntPtr buffer = QuerySystemHandleTable();
            if (buffer == IntPtr.Zero) return null;

            try
            {
                nuint handleCount = *(nuint*)buffer;
                int entrySize = sizeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX);
                IntPtr currentPtr = buffer + IntPtr.Size * 2;
                IntPtr currentProcess = GetCurrentProcess();

                char* pathBuffer = stackalloc char[1024];

                for (nuint i = 0; i < handleCount; i++)
                {
                    SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX* entry = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX*)currentPtr;
                    currentPtr += entrySize;

                    if ((nuint)entry->UniqueProcessId != (nuint)pid) continue;

                    int knownFileType = s_fileObjectTypeIndex;
                    if (knownFileType >= 0 && entry->ObjectTypeIndex != (ushort)knownFileType) continue;

                    if ((entry->GrantedAccess & 0x0001) == 0) continue;

                    if (DuplicateHandle(processHandle, entry->HandleValue, currentProcess, out IntPtr dupHandle, 0, false, DUPLICATE_SAME_ACCESS))
                    {
                        try
                        {
                            if (GetFileType(dupHandle) == FILE_TYPE_DISK)
                            {
                                if (knownFileType < 0)
                                {
                                    s_fileObjectTypeIndex = entry->ObjectTypeIndex;
                                }

                                uint pathLen = GetFinalPathNameByHandleW(dupHandle, pathBuffer, 1024, 0);
                                if (pathLen > 0 && pathLen < 1024)
                                {
                                    ReadOnlySpan<char> rawSpan = new(pathBuffer, (int)pathLen);
                                    ReadOnlySpan<char> extSpan = Path.GetExtension(rawSpan);
                                    if (!extSpan.IsEmpty && _videoExtensionsLookup.Contains(extSpan))
                                    {
                                        return FormatNormalizedPath(pathBuffer, pathLen);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            CloseHandle(dupHandle);
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error inspecting open video files for PID {PID}", pid);
        }
        finally
        {
            CloseHandle(processHandle);
        }

        return null;
    }

    public static List<string> GetOpenFiles(uint pid)
    {
        var files = new List<string>();
        if (!OperatingSystem.IsWindows() || pid == 0) return files;

        HashSet<string> seenFiles = [with(StringComparer.OrdinalIgnoreCase)];
        IntPtr processHandle = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
        if (processHandle == IntPtr.Zero) return files;

        try
        {
            IntPtr buffer = QuerySystemHandleTable();
            if (buffer == IntPtr.Zero) return files;

            try
            {
                nuint handleCount = *(nuint*)buffer;
                int entrySize = sizeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX);
                IntPtr currentPtr = buffer + IntPtr.Size * 2;
                IntPtr currentProcess = GetCurrentProcess();

                char* pathBuffer = stackalloc char[1024];

                for (nuint i = 0; i < handleCount; i++)
                {
                    SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX* entry = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX*)currentPtr;
                    currentPtr += entrySize;

                    if ((nuint)entry->UniqueProcessId != (nuint)pid) continue;

                    int knownFileType = s_fileObjectTypeIndex;
                    if (knownFileType >= 0 && entry->ObjectTypeIndex != (ushort)knownFileType) continue;

                    if ((entry->GrantedAccess & 0x0001) == 0) continue;

                    if (DuplicateHandle(processHandle, entry->HandleValue, currentProcess, out IntPtr dupHandle, 0, false, DUPLICATE_SAME_ACCESS))
                    {
                        try
                        {
                            if (GetFileType(dupHandle) == FILE_TYPE_DISK)
                            {
                                if (knownFileType < 0)
                                {
                                    s_fileObjectTypeIndex = entry->ObjectTypeIndex;
                                }

                                uint pathLen = GetFinalPathNameByHandleW(dupHandle, pathBuffer, 1024, 0);
                                if (pathLen > 0 && pathLen < 1024)
                                {
                                    string path = FormatNormalizedPath(pathBuffer, pathLen);
                                    if (seenFiles.Add(path))
                                    {
                                        files.Add(path);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            CloseHandle(dupHandle);
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error enumerating handles for PID {PID}", pid);
        }
        finally
        {
            CloseHandle(processHandle);
        }

        return files;
    }

    public static ParsedMedia? Apply(ExternalPlayerDefinition player, uint pid)
    {
        if (!OperatingSystem.IsWindows() || pid == 0) return null;

        string? videoFile = TryGetFirstOpenVideoFile(pid);
        if (videoFile is null) return null;

        return MediaTitleResolver.FromFilePath(videoFile, player);
    }
}
