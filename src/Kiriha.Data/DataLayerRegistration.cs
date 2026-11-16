using Kiriha.Core.Abstractions.Extensions;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Data;

/// <summary>
/// Dependency Injection registrations owned by the <c>Kiriha.Data</c> persistence layer:
/// SQLite <see cref="AppDbContext"/> factory, schema initializer, maintenance/cleanup services,
/// and all per-aggregate SQLite repositories.
/// </summary>
public static class DataLayerRegistration
{
    public static IServiceCollection AddKirihaPersistence(
        this IServiceCollection services,
        string dbPath,
        string? imageCachePath = null,
        string? seasonalCachePath = null)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                   .AddInterceptors(new SqlitePragmaConnectionInterceptor());
#if DEBUG
            // Sensitive data logging leaks parameter values (incl. tokens stored on entities)
            // into the EF logger. Keep it strictly out of release builds.
            options.EnableSensitiveDataLogging();
#endif
        });

        services.AddSingleton<DatabaseInitializer>();
        services.AddForwardedSingleton<DatabaseInitializer, IDatabaseInitializer>();
        services.AddSingleton<DatabaseMaintenance>();
        services.AddSingleton<CacheCleanupService>(sp => new CacheCleanupService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            sp.GetRequiredService<IHistoryService>(),
            imageCachePath,
            seasonalCachePath));

        // Per-aggregate SQLite repositories
        services.AddSingleton<IUserAnimeRepository, UserAnimeRepository>();
        services.AddSingleton<IMetadataRepository, MetadataRepository>();
        services.AddSingleton<IMalSearchCacheRepository, MalSearchCacheRepository>();
        services.AddSingleton<IHttpCacheRepository, HttpCacheRepository>();
        services.AddSingleton<IAnimeRelationRepository, AnimeRelationRepository>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<ISyncTaskRepository, SyncTaskRepository>();
        services.AddSingleton<ISeasonalHiddenRepository, SeasonalHiddenRepository>();
        services.AddSingleton<ITorrentFilterRepository, TorrentFilterRepository>();
        services.AddSingleton<IAnimeCountryRepository, AnimeCountryRepository>();
        services.AddSingleton<ITitleMappingRepository, TitleMappingRepository>();
        services.AddSingleton<IAniListMediaMapRepository, AniListMediaMapRepository>();

        services.AddSingleton<IScrobbleProgressStore>(sp =>
        {
            var settingsDir = sp.GetService<IAppPaths>()?.GetSettingsDirPath();
            var legacyPath = settingsDir != null
                ? Path.Combine(settingsDir, AppConstants.System.FileNames.ScrobbleProgress)
                : null;
            return new SqliteScrobbleProgressStore(
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                legacyPath);
        });

        services.AddSingleton<IPlaybackResumeStore>(sp =>
        {
            var settingsDir = sp.GetService<IAppPaths>()?.GetSettingsDirPath();
            var legacyPath = settingsDir != null
                ? Path.Combine(settingsDir, AppConstants.System.FileNames.PlaybackResume)
                : null;
            return new SqlitePlaybackResumeStore(
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                legacyPath);
        });

        return services;
    }
}
