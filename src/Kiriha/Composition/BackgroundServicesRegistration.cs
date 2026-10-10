using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Core;
using Kiriha.Infrastructure.Extensions;
using Kiriha.Services;
using Kiriha.Services.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiriha.Composition;

/// <summary>
/// DI registrations for background/infrastructure services that don't
/// belong to a specific tracker: IPC server, internal player HTTP server,
/// load queue, update checks, notifications, airing info, and maintenance tasks.
///
/// These are separated from <see cref="TrackingServicesRegistration"/> because
/// they are not tracker-specific — they run in the background as
/// <see cref="IHostedService"/> instances or support cross-cutting concerns.
/// </summary>
internal static class BackgroundServicesRegistration
{
    public static IServiceCollection AddKirihaBackgroundServices(this IServiceCollection services)
    {
        // IPC / player HTTP server
        services.AddForwardedSingleton<Kiriha.Infrastructure.Tracking.Integration.InternalPlayerServer, IHostedService>();

        services.AddSingleton<InstanceServer>();
        services.AddForwardedSingleton<InstanceServer, IHostedService>();

        // SyncManager needs to start with the app lifecycle
        services.AddSingleton<IHostedService>(sp => (IHostedService)sp.GetRequiredService<ISyncManager>());

        // Background utilities
        services.AddSingleton<LoadQueueService>();
        services.AddForwardedSingleton<LoadQueueService, ILoadQueueService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<NotificationService>();
        services.AddForwardedSingleton<NotificationService, INotificationService>();
        services.AddSingleton<AiringInfoService>();
        services.AddForwardedSingleton<AiringInfoService, IAiringInfoService>();

        // AnisthesiaService (Discord presence) also runs as IHostedService
        services.AddForwardedSingleton<Kiriha.Infrastructure.Tracking.Integration.AnisthesiaService, IHostedService>();

        // Maintenance tasks
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.AiringSyncMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.UpdateMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.DatabaseMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.MetadataFetchMaintenanceTask>();
        services.AddSingleton<MaintenanceService>();

        // TorrServer streaming engine
        services.AddSingleton<Kiriha.Infrastructure.TorrServer.TorrServerService>();
        services.AddForwardedSingleton<Kiriha.Infrastructure.TorrServer.TorrServerService, ITorrServerService>();

        // Shutdown Handlers
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.TorrServerShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.PlayerResidentShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.BackgroundTasksShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.HostedServicesShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.HistoryShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.DatabaseShutdownHandler>();

        return services;
    }
}

