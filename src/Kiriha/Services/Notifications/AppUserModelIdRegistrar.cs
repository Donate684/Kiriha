using System;
using System.Runtime.InteropServices;
using Serilog;

namespace Kiriha.Services.Notifications;

internal static class AppUserModelIdRegistrar
{
    private const string AumId = "Kiriha";

    public static void Register()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetCurrentProcessExplicitAppUserModelID(AumId);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AppUserModelIdRegistrar: SetCurrentProcessExplicitAppUserModelID failed (non-fatal)");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);
}
