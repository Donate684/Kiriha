namespace Kiriha.Core.Abstractions.Infrastructure;

/// <summary>
/// Provides portable-aware application file and directory paths without coupling Core layers to OS helpers.
/// </summary>
public interface IAppPaths
{
    string GetDbPath();
    string GetMappingFilePath();
    string GetSettingsDirPath();
    string GetImageCachePath();
    string GetSeasonalCachePath();
}
