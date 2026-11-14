using System.Collections.Concurrent;
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

    private const uint FILE_READ_DATA = 0x0001;
    private const uint FILE_WRITE_DATA = 0x0002;
    private const uint FILE_APPEND_DATA = 0x0004;
    private const uint ACCESS_MASK_PIPE_GENERIC = 0x0012019f;
    private const uint ACCESS_MASK_PIPE_READ = 0x00120189;

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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelSynchronousIo(IntPtr hThread);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentThread();

    private static readonly Lock s_queryLock = new();
    private static HandleQueryWorker? s_activeWorker;

    private sealed class HandleQueryWorkItem(IntPtr fileHandle)
    {
        public IntPtr FileHandle { get; } = fileHandle;
        public ManualResetEventSlim DoneEvent { get; } = new(false);
        public volatile string? ResultPath;
        private int _refCount = 2; // 1 for caller, 1 for worker

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                if (FileHandle != IntPtr.Zero)
                {
                    CloseHandle(FileHandle);
                }
                DoneEvent.Dispose();
            }
        }
    }

    private sealed class HandleQueryWorker
    {
        private readonly BlockingCollection<HandleQueryWorkItem> _queue = new();
        private readonly Lock _threadHandleLock = new();
        private IntPtr _workerThreadHandle;
        private bool _closed;

        public HandleQueryWorker()
        {
            var thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "HandleQueryWorker"
            };
            thread.Start();
        }

        public bool TryEnqueue(HandleQueryWorkItem item)
        {
            try
            {
                return _queue.TryAdd(item);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void AbandonAndCancelIo()
        {
            try
            {
                _queue.CompleteAdding();
            }
            catch
            {
                // Ignore if already completed/disposed
            }

            lock (_threadHandleLock)
            {
                if (!_closed && _workerThreadHandle != IntPtr.Zero)
                {
                    CancelSynchronousIo(_workerThreadHandle);
                }
            }
        }

        private void RunLoop()
        {
            try
            {
                lock (_threadHandleLock)
                {
                    if (!_closed)
                    {
                        DuplicateHandle(
                            GetCurrentProcess(),
                            GetCurrentThread(),
                            GetCurrentProcess(),
                            out _workerThreadHandle,
                            0,
                            false,
                            DUPLICATE_SAME_ACCESS);
                    }
                }

                char* pathBuffer = stackalloc char[1024];
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        if (GetFileType(item.FileHandle) == FILE_TYPE_DISK)
                        {
                            uint pathLen = GetFinalPathNameByHandleW(item.FileHandle, pathBuffer, 1024, 0);
                            if (pathLen > 0 && pathLen < 1024)
                            {
                                item.ResultPath = FormatNormalizedPath(pathBuffer, pathLen);
                            }
                        }
                    }
                    catch
                    {
                        // Guard worker thread against any P/Invoke or marshaling exception
                    }
                    finally
                    {
                        try { item.DoneEvent.Set(); } catch { }
                        item.Release();
                    }
                }
            }
            catch
            {
                // Never allow unhandled exceptions on background thread
            }
            finally
            {
                lock (_threadHandleLock)
                {
                    _closed = true;
                    if (_workerThreadHandle != IntPtr.Zero)
                    {
                        CloseHandle(_workerThreadHandle);
                        _workerThreadHandle = IntPtr.Zero;
                    }
                }

                try { _queue.Dispose(); } catch { }
            }
        }
    }

    /// <summary>
    /// Queries disk file path for <paramref name="dupHandle"/> on a persistent background worker
    /// and takes ownership of closing <paramref name="dupHandle"/> via reference counting so a
    /// timed-out kernel call can never race against handle recycling.
    /// </summary>
    private static string? QueryDiskFilePathWithTimeout(IntPtr dupHandle, int timeoutMs = 100)
    {
        var item = new HandleQueryWorkItem(dupHandle);
        try
        {
            lock (s_queryLock)
            {
                var worker = s_activeWorker ??= new HandleQueryWorker();
                if (!worker.TryEnqueue(item))
                {
                    worker = s_activeWorker = new HandleQueryWorker();
                    if (!worker.TryEnqueue(item))
                    {
                        // Worker never took ownership — release worker's ref here
                        item.Release();
                        return null;
                    }
                }

                if (!item.DoneEvent.Wait(timeoutMs))
                {
                    s_activeWorker = null;
                    worker.AbandonAndCancelIo();
                    Log.Debug("Handle query timed out on handle 0x{Handle:X}, cancelled synchronous I/O", dupHandle);
                    return null;
                }
            }

            return item.ResultPath;
        }
        finally
        {
            item.Release();
        }
    }

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

                for (nuint i = 0; i < handleCount; i++)
                {
                    SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX* entry = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX*)currentPtr;
                    currentPtr += entrySize;

                    if ((nuint)entry->UniqueProcessId != (nuint)pid) continue;

                    int knownFileType = s_fileObjectTypeIndex;
                    if (knownFileType >= 0 && entry->ObjectTypeIndex != (ushort)knownFileType) continue;

                    if ((entry->GrantedAccess & FILE_READ_DATA) == 0) continue;
                    if ((entry->GrantedAccess & (FILE_WRITE_DATA | FILE_APPEND_DATA)) != 0) continue;
                    if (entry->GrantedAccess == ACCESS_MASK_PIPE_GENERIC || entry->GrantedAccess == ACCESS_MASK_PIPE_READ) continue;

                    if (DuplicateHandle(processHandle, entry->HandleValue, currentProcess, out IntPtr dupHandle, 0, false, DUPLICATE_SAME_ACCESS))
                    {
                        // Ownership of dupHandle is transferred to QueryDiskFilePathWithTimeout.
                        string? filePath = QueryDiskFilePathWithTimeout(dupHandle);
                        if (filePath != null)
                        {
                            if (knownFileType < 0)
                            {
                                s_fileObjectTypeIndex = entry->ObjectTypeIndex;
                            }

                            ReadOnlySpan<char> extSpan = Path.GetExtension(filePath.AsSpan());
                            if (!extSpan.IsEmpty && _videoExtensionsLookup.Contains(extSpan))
                            {
                                return filePath;
                            }
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

                for (nuint i = 0; i < handleCount; i++)
                {
                    SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX* entry = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX*)currentPtr;
                    currentPtr += entrySize;

                    if ((nuint)entry->UniqueProcessId != (nuint)pid) continue;

                    int knownFileType = s_fileObjectTypeIndex;
                    if (knownFileType >= 0 && entry->ObjectTypeIndex != (ushort)knownFileType) continue;

                    if ((entry->GrantedAccess & FILE_READ_DATA) == 0) continue;
                    if ((entry->GrantedAccess & (FILE_WRITE_DATA | FILE_APPEND_DATA)) != 0) continue;
                    if (entry->GrantedAccess == ACCESS_MASK_PIPE_GENERIC || entry->GrantedAccess == ACCESS_MASK_PIPE_READ) continue;

                    if (DuplicateHandle(processHandle, entry->HandleValue, currentProcess, out IntPtr dupHandle, 0, false, DUPLICATE_SAME_ACCESS))
                    {
                        // Ownership of dupHandle is transferred to QueryDiskFilePathWithTimeout.
                        string? filePath = QueryDiskFilePathWithTimeout(dupHandle);
                        if (filePath != null)
                        {
                            if (knownFileType < 0)
                            {
                                s_fileObjectTypeIndex = entry->ObjectTypeIndex;
                            }

                            if (seenFiles.Add(filePath))
                            {
                                files.Add(filePath);
                            }
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
