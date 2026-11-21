using System.Diagnostics;
using Kiriha.Composition;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Tracking;
using Kiriha.Core.Streaming;
using Kiriha.Infrastructure.Platform;
using Kiriha.Infrastructure.Tracking;
using Kiriha.Services.Metadata;
using Kiriha.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class AppStartupCoordinator
{
    public static IServiceProvider BuildServiceProvider(bool isPlayerMode)
    {
        var sw = Stopwatch.StartNew();
        var services = new ServiceCollection();
        if (isPlayerMode)
            ConfigurePlayerServices(services);
        else
            ConfigureServices(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
#if DEBUG
            ValidateOnBuild = true,
#endif
            ValidateScopes = true,
        });
        Log.Information(
            "StartupTiming: service provider built mode={Mode} elapsedMs={ElapsedMs}",
            isPlayerMode ? "player" : "app",
            sw.ElapsedMilliseconds);
        return provider;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        PathHelper.EnsureDirectoriesExist();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IBackgroundTaskSupervisor, BackgroundTaskSupervisor>();

        services
            .AddKirihaData(PathHelper.GetDbPath())
            .AddKirihaTracking()
            .AddKirihaTrackingInfrastructure()
            .AddKirihaBackgroundServices()
            .AddKirihaStreaming()
            .AddKirihaUi();
    }

    private static readonly TimeSpan PooledHandlerLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TorrServerClientTimeout = TimeSpan.FromSeconds(10);

    private static void ConfigurePlayerServices(IServiceCollection services)
    {
        PathHelper.EnsureDirectoriesExist();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.SettingsShutdownHandler>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ILocalizer>(sp => sp.GetRequiredService<LocalizationService>());
        services.AddSingleton<Kiriha.Mpv.UI.Services.Player.IPlayerMediaMetadataResolver, Kiriha.Mpv.UI.Services.Player.FilenamePlayerMediaMetadataResolver>();
        services.AddSingleton<IPlaybackResumeStore, Kiriha.Mpv.UI.Services.Player.FilePlaybackResumeStore>();
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.ManualMappingShutdownHandler>();
        services.AddHttpClient("TorrServerClient", c => c.Timeout = TorrServerClientTimeout)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = PooledHandlerLifetime,
                });
        services.AddSingleton<ITorrServerService>(sp =>
            new Kiriha.Infrastructure.TorrServer.TorrServerService(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("TorrServerClient")));
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.TorrServerShutdownHandler>();
    }
}


