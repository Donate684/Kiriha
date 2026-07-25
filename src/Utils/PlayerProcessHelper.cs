using System;
using System.Diagnostics;

namespace Kiriha.Utils;

public static class PlayerProcessHelper
{
    public static void LaunchPlayer()
    {
        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(assemblyPath) || string.IsNullOrEmpty(processPath)) return;

        var isDotnet = System.IO.Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = isDotnet ? $"\"{assemblyPath}\" --player" : "--player",
            UseShellExecute = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to launch player process");
        }
    }
}
