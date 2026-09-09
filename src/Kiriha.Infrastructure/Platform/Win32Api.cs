using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Kiriha.Infrastructure.Platform;

public static class Win32Api
{
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    public static string GetWindowTitle(IntPtr hWnd)
    {
        if (!OperatingSystem.IsWindows() || hWnd == IntPtr.Zero) return string.Empty;

        int length = GetWindowTextLength(hWnd);
        if (length <= 0) return string.Empty;

        StringBuilder sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static IntPtr FindMainWindow(uint targetPid)
    {
        if (!OperatingSystem.IsWindows() || targetPid == 0) return IntPtr.Zero;

        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == targetPid && IsWindowVisible(hWnd))
            {
                int len = GetWindowTextLength(hWnd);
                if (len > 0)
                {
                    found = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }
}
