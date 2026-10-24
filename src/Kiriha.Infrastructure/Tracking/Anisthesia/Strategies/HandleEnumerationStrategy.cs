using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Kiriha.Core.Domain.Models;
using Kiriha.Utils.Parsing;
using Serilog;

namespace Kiriha.Infrastructure.Tracking.Anisthesia.Strategies;

public class HandleEnumerationStrategy
{
    private static readonly FrozenSet<string> _videoExtensions = FrozenSet.ToFrozenSet([".mkv", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".ogm", ".webm", ".ts", ".m4v", ".m2ts"], StringComparer.OrdinalIgnoreCase);

    // P/Invoke constants and structures
    private const int SystemExtendedHandleInformation = 64;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    private const int STATUS_SUCCESS = 0;

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

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle, uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(IntPtr hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr hFile);

    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const uint FILE_TYPE_DISK = 0x0001;

    public static unsafe List<string> GetOpenFiles(uint pid)
    {
        var files = new List<string>();
        if (!OperatingSystem.IsWindows() || pid == 0) return files;

        HashSet<string> seenFiles = [with(StringComparer.OrdinalIgnoreCase)];
        IntPtr processHandle = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
        if (processHandle == IntPtr.Zero) return files;

        try
        {
            int bufferSize = 1024 * 1024; // 1MB initial
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int returnLength;
                int status;

                while ((status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out returnLength)) == STATUS_INFO_LENGTH_MISMATCH)
                {
                    bufferSize = Math.Max(returnLength + 65536, bufferSize * 2);
                    Marshal.FreeHGlobal(buffer);
                    buffer = Marshal.AllocHGlobal(bufferSize);
                }

                if (status != STATUS_SUCCESS) return files;

                long handleCount = IntPtr.Size == 8 ? Marshal.ReadInt64(buffer) : Marshal.ReadInt32(buffer);
                int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
                IntPtr currentPtr = buffer + IntPtr.Size * 2; // Skip NumberOfHandles and Reserved

                char[] pathBuffer = System.Buffers.ArrayPool<char>.Shared.Rent(32768);
                try
                {
                    for (long i = 0; i < handleCount; i++)
                    {
                        SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX* entry = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX*)currentPtr;
                        currentPtr += entrySize;

                        if ((nuint)entry->UniqueProcessId != (nuint)pid) continue;

                        // Simple access mask check (FILE_READ_DATA = 0x0001)
                        if ((entry->GrantedAccess & 0x0001) == 0) continue;

                        if (DuplicateHandle(processHandle, entry->HandleValue, GetCurrentProcess(), out IntPtr dupHandle, 0, false, DUPLICATE_SAME_ACCESS))
                        {
                            try
                            {
                                if (GetFileType(dupHandle) == FILE_TYPE_DISK)
                                {
                                    uint pathLen = GetFinalPathNameByHandle(dupHandle, pathBuffer, (uint)pathBuffer.Length, 0);
                                    if (pathLen > 0 && pathLen < pathBuffer.Length)
                                    {
                                        string path;
                                        if (pathLen >= 4 && pathBuffer[0] == '\\' && pathBuffer[1] == '\\' && pathBuffer[2] == '?' && pathBuffer[3] == '\\')
                                        {
                                            if (pathLen >= 8 && (pathBuffer[4] == 'U' || pathBuffer[4] == 'u') &&
                                                                (pathBuffer[5] == 'N' || pathBuffer[5] == 'n') &&
                                                                (pathBuffer[6] == 'C' || pathBuffer[6] == 'c') &&
                                                                pathBuffer[7] == '\\')
                                            {
                                                path = "\\\\" + new string(pathBuffer, 8, (int)pathLen - 8);
                                            }
                                            else
                                            {
                                                path = new string(pathBuffer, 4, (int)pathLen - 4);
                                            }
                                        }
                                        else
                                        {
                                            path = new string(pathBuffer, 0, (int)pathLen);
                                        }

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
                    System.Buffers.ArrayPool<char>.Shared.Return(pathBuffer);
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
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

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static ParsedMedia? Apply(AnisthesiaPlayer player, uint pid)
    {
        if (!OperatingSystem.IsWindows() || pid == 0) return null;

        var files = GetOpenFiles(pid);

        foreach (var file in files)
        {
            string ext = System.IO.Path.GetExtension(file);
            if (_videoExtensions.Contains(ext))
            {
                string filename = System.IO.Path.GetFileName(file);

                // Use AnimeReleaseParser to parse the filename
                var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(filename);

                string animeTitle;
                bool isEmber = file.Contains("EMBER", StringComparison.OrdinalIgnoreCase) || EmberTitleResolver.ScanFileForEmber(file);

                if (!string.IsNullOrEmpty(parsed.Title) && !isEmber)
                {
                    animeTitle = parsed.Title;
                }
                else
                {
                    string meaningfulDir = EmberTitleResolver.GetMeaningfulDirectoryName(file);
                    if (!string.IsNullOrEmpty(meaningfulDir))
                    {
                        var dirParsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(meaningfulDir);
                        animeTitle = !string.IsNullOrEmpty(dirParsed.Title) ? dirParsed.Title : meaningfulDir;
                    }
                    else
                    {
                        animeTitle = !string.IsNullOrEmpty(parsed.Title) ? parsed.Title : Path.GetFileNameWithoutExtension(filename);
                    }
                }

                return new ParsedMedia
                {
                    OriginalTitle = filename,
                    AnimeTitle = animeTitle,
                    EpisodeTitle = parsed.EpisodeTitle,
                    Episode = parsed.Episode,
                    Season = parsed.Season?.ToString() ?? string.Empty,
                    Group = parsed.ReleaseGroup,
                    IsPlaying = true,
                    ProcessName = player.Executables.FirstOrDefault() ?? ""
                };
            }
        }

        return null;
    }
}

