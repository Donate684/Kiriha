using Kiriha.Infrastructure.Caching;
using Kiriha.Core.Abstractions.Extensions;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Constants;
using Kiriha.Data;
using Kiriha.Infrastructure;
using Kiriha.Infrastructure.Platform;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Image;
using Kiriha.Services.Metadata;
using Kiriha.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Composition;

/// <summary>
/// DI registrations for persistence and host-level data/caching services:
/// delegates SQLite &amp; repository wiring to <see cref="DataLayerRegistration.AddKirihaPersistence"/>
/// and registers application settings, UI dispatcher, localization, and image/seasonal caches.
/// </summary>
internal static class DataServicesRegistration
{
    private static readonly TimeSpan PooledHandlerLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ImageClientTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddKirihaData(this IServiceCollection services, string dbPath)
    {
        services.AddKirihaPersistence(
            dbPath,
            PathHelper.GetImageCachePath(),
            PathHelper.GetSeasonalCachePath());

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<SettingsService>();
        services.AddForwardedSingleton<SettingsService, ISettingsService>();
        services.AddSingleton<AppReadinessService>();
        services.AddSingleton<LocalizationService>();
        services.AddForwardedSingleton<LocalizationService, ILocalizer>();

        services.AddHttpClient("ImageClient", c =>
        {
            c.Timeout = ImageClientTimeout;
            c.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = PooledHandlerLifetime,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        });

        services.AddSingleton<ImageCacheService>(sp =>
        {
            var rewriters = sp.GetServices<IImageUrlRewriter>().ToList();
            IImageUrlRewriter? rewriter = rewriters.Count switch
            {
                0 => null,
                1 => rewriters[0],
                _ => new CompositeImageUrlRewriter(rewriters)
            };

            return new ImageCacheService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IBackgroundTaskSupervisor>(),
                rewriter);
        });
        services.AddForwardedSingleton<ImageCacheService, IImageCacheService>();
        services.AddSingleton<PosterBatchDownloader>();
        services.AddSingleton<SeasonalCacheStore>();

        return services;
    }
}
