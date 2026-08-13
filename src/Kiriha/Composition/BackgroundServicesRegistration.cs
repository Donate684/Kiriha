using Kiriha.Services;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Settings;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Integration;
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
        services.AddSingleton<InternalPlayerServer>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<InternalPlayerServer>());

        services.AddSingleton<InstanceServer>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<InstanceServer>());

        // SyncManager needs to start with the app lifecycle
        services.AddSingleton<IHostedService>(sp => (IHostedService)sp.GetRequiredService<Kiriha.Core.Services.ISyncManager>());

        // Background utilities
        services.AddSingleton<LoadQueueService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<Kiriha.Core.Services.INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<AiringInfoService>();

        // AnisthesiaService (Discord presence) also runs as IHostedService
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<Kiriha.Core.Tracking.Integration.AnisthesiaService>());

        // Maintenance tasks
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.RssMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.AiringSyncMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.UpdateMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.DatabaseMaintenanceTask>();
        services.AddSingleton<Services.Maintenance.IMaintenanceTask, Services.Maintenance.MetadataFetchMaintenanceTask>();
        services.AddSingleton<MaintenanceService>();

        // Shutdown Handlers
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.PlayerResidentShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.BackgroundTasksShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.HostedServicesShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.HistoryShutdownHandler>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.DatabaseShutdownHandler>();

        return services;
    }
}
