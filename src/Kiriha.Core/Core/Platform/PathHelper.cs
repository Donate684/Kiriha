using Kiriha.Core.Domain.Constants;
using System;
using System.IO;

namespace Kiriha.Core.Platform;

public static class PathHelper
{
    private static readonly string AppDir = AppContext.BaseDirectory;
    private static readonly string PortableDataDir = Path.Combine(AppDir, "data");

    public static bool IsPortable => Directory.Exists(PortableDataDir);

    private static string BasePath => IsPortable
        ? PortableDataDir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.System.AppName);

    public static string GetDbPath() => Path.Combine(BasePath, AppConstants.System.FileNames.Database);

    public static string GetLogsPath() => Path.Combine(BasePath, AppConstants.System.FileNames.LogsDir);

    public static string GetImageCachePath() => Path.Combine(BasePath, AppConstants.System.FileNames.CacheDir);

    public static string GetMappingFilePath() => Path.Combine(BasePath, AppConstants.System.FileNames.Mappings);

    public static string GetSettingsPath() => Path.Combine(BasePath, AppConstants.System.FileNames.Settings);

    public static string GetTempPath() => Path.Combine(Path.GetTempPath(), AppConstants.System.AppName);

    /// <summary>
    /// Folder where user-supplied icons for custom share links are copied.
    /// Files outlive the original picked file (we don't reference it directly).
    /// </summary>
    public static string GetCustomIconsPath() => Path.Combine(BasePath, "custom_icons");

    /// <summary>
    /// Disk location of the persistent seasonal-anime cache (one JSON per season).
    /// </summary>
    public static string GetSeasonalCachePath() => Path.Combine(BasePath, "seasonal_cache");

    public static string GetAppDir() => AppDir;

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(BasePath);
        Directory.CreateDirectory(GetLogsPath());
        Directory.CreateDirectory(GetImageCachePath());
    }
}
