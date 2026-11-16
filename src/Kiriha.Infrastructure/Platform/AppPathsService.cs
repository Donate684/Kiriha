using Kiriha.Core.Abstractions.Infrastructure;

namespace Kiriha.Infrastructure.Platform;

public sealed class AppPathsService : IAppPaths
{
    public string GetDbPath() => PathHelper.GetDbPath();
    public string GetMappingFilePath() => PathHelper.GetMappingFilePath();
    public string GetSettingsDirPath() => PathHelper.GetSettingsDirPath();
    public string GetImageCachePath() => PathHelper.GetImageCachePath();
    public string GetSeasonalCachePath() => PathHelper.GetSeasonalCachePath();
}
