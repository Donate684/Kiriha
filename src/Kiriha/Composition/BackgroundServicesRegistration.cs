using Kiriha.Core.Abstractions.Extensions;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiriha.Composition;

/// <summary>
/// DI registrations for background/infrastructure services that don't
/// belong to a specific tracker: IPC server, internal player HTTP server,
/// load queue, update checks, notifications, and maintenance tasks.
///
/// These are separated from <c>TrackingServicesRegistration</c> because
/// they are not tracker-specific — they run in the background as
/// <see cref="IHostedService"/> instances or support cross-cutting concerns.
/// </summary>
internal static class BackgroundServicesRegistration
{
    private static readonly TimeSpan PooledHandlerLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TorrServerClientTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddKirihaBackgroundServices(this IServiceCollection services)
    {
        // Core tracking & scrobbler service runs in background as IHostedService
        services.AddForwardedSingleton<TrackingService, IHostedService>();

        // IPC / player HTTP server
        services.AddForwardedSingleton<Kiriha.Infrastructure.Tracking.Integration.InternalPlayerServer, IHostedService>();

        services.AddSingleton<InstanceServer>();
        services.AddForwardedSingleton<InstanceServer, IHostedService>();

        // SyncManager needs to start with the app lifecycle
        services.AddForwardedSingleton<SyncManager, IHostedService>();

        // Background utilities
        services.AddSingleton<LoadQueueService>();
        services.AddForwardedSingleton<LoadQueueService, ILoadQueueService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<NotificationService>();
        services.AddForwardedSingleton<NotificationService, INotificationService>();

        // ExternalMediaDetectorService also runs as IHostedService
        services.AddForwardedSingleton<Kiriha.Infrastructure.Tracking.Integration.ExternalMediaDetectorService, IHostedService>();

        // Maintenance tasks
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.AiringSyncMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.UpdateMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.DatabaseMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.MetadataFetchMaintenanceTask>();
        services.AddSingleton<MaintenanceService>();

        // TorrServer streaming engine
        services.AddHttpClient("TorrServerClient", c => c.Timeout = TorrServerClientTimeout)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = PooledHandlerLifetime,
                });
        services.AddSingleton<Kiriha.Infrastructure.TorrServer.TorrServerService>(sp =>
            new Kiriha.Infrastructure.TorrServer.TorrServerService(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("TorrServerClient")));
        services.AddForwardedSingleton<Kiriha.Infrastructure.TorrServer.TorrServerService, ITorrServerService>();

        // Close both player modes before stopping tracking, TorrServer, and the local stores.
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.PlayerResidentShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.HostedPlayerWindowsShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.HostedServicesShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.BackgroundTasksShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.TorrServerShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.ManualMappingShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.SettingsShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.HistoryShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.DatabaseShutdownHandler>();

        return services;
    }
}
