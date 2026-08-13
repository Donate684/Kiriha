using System.Diagnostics;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.Win32;

namespace Kiriha.Core.Tracking.Core;

public static class StartupService
{
    private const string AppName = "Kiriha";
    private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static void EnableStartup(bool launchMinimized)
    {
        if (!System.OperatingSystem.IsWindows())
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

    public static void DisableStartup()
    {
        if (!System.OperatingSystem.IsWindows())
            return;

        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
        if (key != null)
        {
            key.DeleteValue(AppName, false);
        }
    }
}
