using System;
using Microsoft.Win32;
using Serilog;

namespace Kiriha.Services.Windows;

internal static class WindowsRegistryHelper
{
    public static void RegAdd(RegistryKey hive, string keyPath, string valueName, object value, RegistryValueKind kind = RegistryValueKind.String)
    {
        using var key = hive.CreateSubKey(keyPath, true);
        if (key != null)
        {
            key.SetValue(valueName, value, kind);
        }
    }

    public static void RegSetDefault(RegistryKey hive, string keyPath, object value)
    {
        using var key = hive.CreateSubKey(keyPath, true);
        if (key != null)
        {
            key.SetValue("", value);
        }
    }

    public static void DeleteKeyTree(RegistryKey hive, string keyPath)
    {
        try
        {
            hive.DeleteSubKeyTree(keyPath, false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to delete registry key tree: {KeyPath}", keyPath);
        }
    }

    public static void DeleteValueSafe(RegistryKey hive, string keyPath, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath, true);
            key?.DeleteValue(valueName, false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to delete registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
        }
    }
}
