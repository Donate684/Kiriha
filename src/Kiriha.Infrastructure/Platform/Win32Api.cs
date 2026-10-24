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
        public IntPtr FallbackHwnd;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowTextW(IntPtr hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowTextLengthW(IntPtr hWnd);

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
            if (GetWindow(hWnd, GW_OWNER) == IntPtr.Zero)
            {
                ctx->FoundHwnd = hWnd;
                return 0;
            }

            if (ctx->FallbackHwnd == IntPtr.Zero)
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

    public static IntPtr FindMainWindow(uint targetPid)
    {
        if (!OperatingSystem.IsWindows() || targetPid == 0) return IntPtr.Zero;

        FindWindowContext context = new()
        {
            TargetPid = targetPid,
            FoundHwnd = IntPtr.Zero,
            FallbackHwnd = IntPtr.Zero
        };

        EnumWindows(&EnumWindowsCallback, (IntPtr)(&context));
        return context.FoundHwnd != IntPtr.Zero ? context.FoundHwnd : context.FallbackHwnd;
    }
}
