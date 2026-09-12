using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Kiriha.Core.Abstractions.Services;
using Microsoft.Win32;

namespace Kiriha.Infrastructure.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupManager : IStartupManager
{
    private const string AppName = Kiriha.Core.Domain.Constants.AppConstants.System.AppName;
    private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public void EnableStartup(bool launchMinimized)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
        if (key != null)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return;

            var command = $"\"{exePath}\"";
            if (launchMinimized)
            {
                command += " --minimized";
            }
            key.SetValue(AppName, command);
        }
    }

    public void DisableStartup()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
        key?.DeleteValue(AppName, false);
    }
}
