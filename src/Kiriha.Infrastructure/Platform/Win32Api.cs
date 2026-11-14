using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kiriha.Infrastructure.Platform;

public static unsafe partial class Win32Api
{
    private const uint GW_OWNER = 4;

    private struct FindWindowContext
    {
        public uint TargetPid;
        public IntPtr FoundHwnd;
        public IntPtr ClassMatchedHwnd;
        public IntPtr FallbackHwnd;
        public IntPtr ClassesGCHandle;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowTextW(IntPtr hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowTextLengthW(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetClassNameW(IntPtr hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumWindowsCallback(IntPtr hWnd, IntPtr lParam)
    {
        FindWindowContext* ctx = (FindWindowContext*)lParam;
        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == ctx->TargetPid && IsWindowVisible(hWnd) && GetWindowTextLengthW(hWnd) > 0)
        {
            bool isOwnerless = GetWindow(hWnd, GW_OWNER) == IntPtr.Zero;
            bool classMatched = false;

            if (ctx->ClassesGCHandle != IntPtr.Zero)
            {
                try
                {
                    var gch = GCHandle.FromIntPtr(ctx->ClassesGCHandle);
                    if (gch.Target is IReadOnlyList<string> allowedClasses && allowedClasses.Count > 0)
                    {
                        char* classNameBuffer = stackalloc char[256];
                        int classLen = GetClassNameW(hWnd, classNameBuffer, 256);
                        if (classLen > 0)
                        {
                            string className = new string(classNameBuffer, 0, classLen);
                            foreach (var pattern in allowedClasses)
                            {
                                if (pattern.StartsWith('^'))
                                {
                                    if (System.Text.RegularExpressions.Regex.IsMatch(className, pattern))
                                    {
                                        classMatched = true;
                                        break;
                                    }
                                }
                                else if (string.Equals(className, pattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    classMatched = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            if (classMatched)
            {
                if (isOwnerless)
                {
                    ctx->ClassMatchedHwnd = hWnd;
                    ctx->FoundHwnd = hWnd;
                    return 0; // Preferred exact ownerless match
                }

                if (ctx->ClassMatchedHwnd == IntPtr.Zero)
                {
                    ctx->ClassMatchedHwnd = hWnd;
                }
            }
            else if (isOwnerless)
            {
                if (ctx->FoundHwnd == IntPtr.Zero) ctx->FoundHwnd = hWnd;
            }
            else if (ctx->FallbackHwnd == IntPtr.Zero)
            {
                ctx->FallbackHwnd = hWnd;
            }
        }

        return 1;
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        if (!OperatingSystem.IsWindows() || hWnd == IntPtr.Zero) return string.Empty;

        int length = GetWindowTextLengthW(hWnd);
        if (length <= 0) return string.Empty;

        if (length < 512)
        {
            char* buffer = stackalloc char[length + 1];
            int copied = GetWindowTextW(hWnd, buffer, length + 1);
            return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
        }

        char[] rented = ArrayPool<char>.Shared.Rent(length + 1);
        try
        {
            fixed (char* buffer = rented)
            {
                int copied = GetWindowTextW(hWnd, buffer, length + 1);
                return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    public static IntPtr FindMainWindow(uint targetPid, IReadOnlyList<string>? allowedClasses = null)
    {
        if (!OperatingSystem.IsWindows() || targetPid == 0) return IntPtr.Zero;

        GCHandle gcHandle = default;
        bool hasClasses = allowedClasses is { Count: > 0 };
        if (hasClasses)
        {
            gcHandle = GCHandle.Alloc(allowedClasses);
        }

        try
        {
            FindWindowContext context = new()
            {
                TargetPid = targetPid,
                FoundHwnd = IntPtr.Zero,
                ClassMatchedHwnd = IntPtr.Zero,
                FallbackHwnd = IntPtr.Zero,
                ClassesGCHandle = hasClasses ? GCHandle.ToIntPtr(gcHandle) : IntPtr.Zero
            };

            EnumWindows(&EnumWindowsCallback, (IntPtr)(&context));

            if (hasClasses && context.ClassMatchedHwnd != IntPtr.Zero)
            {
                return context.ClassMatchedHwnd;
            }

            return context.FoundHwnd != IntPtr.Zero ? context.FoundHwnd : context.FallbackHwnd;
        }
        finally
        {
            if (gcHandle.IsAllocated)
            {
                gcHandle.Free();
            }
        }
    }
}
