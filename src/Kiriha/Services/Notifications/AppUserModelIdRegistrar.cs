using System.Runtime.InteropServices;
using Serilog;

namespace Kiriha.Services.Notifications;

internal static class AppUserModelIdRegistrar
{
    private const string AumId = Kiriha.Core.Domain.Constants.AppConstants.System.AppName;

    public static void Register()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetCurrentProcessExplicitAppUserModelID(AumId);

                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AumId}");
                    if (key != null)
                    {
                        key.SetValue("DisplayName", Kiriha.Core.Domain.Constants.AppConstants.System.AppName);
                        key.SetValue("ShowInSettings", 1, Microsoft.Win32.RegistryValueKind.DWord);

                        var appDataIcon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Kiriha.Core.Domain.Constants.AppConstants.System.AppName, "kiriha_notif.png");
                        if (!File.Exists(appDataIcon))
                        {
                            var localAsset = Path.Combine(AppContext.BaseDirectory, "Assets", "kiriha_notif.png");
                            if (File.Exists(localAsset))
                            {
                                try { File.Copy(localAsset, appDataIcon, overwrite: true); } catch { }
                            }
                        }

                        if (File.Exists(appDataIcon))
                        {
                            key.SetValue("IconUri", appDataIcon);
                        }
                        else
                        {
                            var processPath = Environment.ProcessPath;
                            if (!string.IsNullOrEmpty(processPath))
                            {
                                key.SetValue("IconUri", processPath);
                            }
                        }
                    }
                }
                catch (Exception regEx)
                {
                    Log.Debug(regEx, "AppUserModelIdRegistrar: Registry registration failed (non-fatal)");
                }
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
