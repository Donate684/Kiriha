using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Shared;
using Kiriha.Infrastructure;
using Kiriha.Infrastructure.Extensions;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Mapping;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using Kiriha.Services.Franchise;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Composition;

/// <summary>
/// DI registrations for the data access layer:
///   * EF Core context factory (Sqlite, NoTracking, shared cache)
///   * Settings service
///   * SQLite-backed per-aggregate repositories (UserAnime, Metadata,
///     MalSearchCache, HttpCache, EpisodeReleases, History, SyncTasks)
///   * In-memory caches and per-feature data services
///   * Authentication services for upstream APIs
///   * HTTP client wiring for MAL/Shiki/RSS pipelines
///
/// Kept as an extension method instead of a free-form block in
/// <c>App.ConfigureServices</c> so the lifetime decisions for the data layer
/// live next to each other and a future PR adding a new repo touches one file
/// instead of editing the App constructor.
/// </summary>
internal static class DataServicesRegistration
{
    public static IServiceCollection AddKirihaData(this IServiceCollection services, string dbPath)
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

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<SettingsService>();
        services.AddForwardedSingleton<SettingsService, ISettingsService>();
        services.AddSingleton<AppReadinessService>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddForwardedSingleton<DatabaseInitializer, IDatabaseInitializer>();
        services.AddSingleton<DatabaseMaintenance>();
        services.AddSingleton<CacheCleanupService>();

        // Per-aggregate repositories. Replaces the monolithic DatabaseService —
        // every consumer now depends on the narrowest interface that covers its
        // queries, so a future swap of the storage layer can happen one
        // aggregate at a time.
        services.AddSingleton<AnimeRepository>();
        services.AddForwardedSingleton<AnimeRepository, IAnimeRepository>();
        services.AddSingleton<IUserAnimeRepository, UserAnimeRepository>();
        services.AddSingleton<IMetadataRepository, MetadataRepository>();
        services.AddSingleton<IMalSearchCacheRepository, MalSearchCacheRepository>();
        services.AddSingleton<IHttpCacheRepository, HttpCacheRepository>();
        services.AddSingleton<IEpisodeReleaseRepository, EpisodeReleaseRepository>();
        services.AddSingleton<IAnimeRelationRepository, AnimeRelationRepository>();
        services.AddSingleton<FranchiseService>();
        services.AddForwardedSingleton<FranchiseService, IFranchiseService>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<ISyncTaskRepository, SyncTaskRepository>();
        services.AddSingleton<ISeasonalHiddenRepository, SeasonalHiddenRepository>();
        services.AddSingleton<ITorrentFilterRepository, TorrentFilterRepository>();
        services.AddSingleton<IAnimeCountryRepository, AnimeCountryRepository>();
        services.AddSingleton<LocalizationService>();
        services.AddForwardedSingleton<LocalizationService, ILocalizer>();
        services.AddSingleton<ShikiMetadataService>();
        services.AddForwardedSingleton<ShikiMetadataService, IShikiMetadataService>();
        services.AddHttpClient("ImageClient", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
        });
        services.AddSingleton<ImageCacheService>(sp => new ImageCacheService(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IBackgroundTaskSupervisor>(),
            sp.GetService<IImageUrlRewriter>()));
        services.AddForwardedSingleton<ImageCacheService, IImageCacheService>();
        services.AddSingleton<PosterBatchDownloader>();
        services.AddSingleton<SeasonalCacheStore>();
        services.AddSingleton<HistoryService>();
        services.AddForwardedSingleton<HistoryService, IHistoryService>();
        services.AddSingleton<ManualMappingService>();
        services.AddSingleton<RecognitionCache>();
        services.AddForwardedSingleton<RecognitionCache, IRecognitionCache>();
        services.AddSingleton<MappingService>();
        services.AddForwardedSingleton<MappingService, IMappingService>();

        return services;
    }
}



