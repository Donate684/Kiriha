using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Tracking;
using Kiriha.Services.Data.Settings;
using Kiriha.Services.Data.Metadata;
using System;
using System.Diagnostics;
using Kiriha.Composition;
using Kiriha.Core.Platform;
using Kiriha.Services.Data;
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
            ValidateOnBuild = !isPlayerMode,
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

        services.AddSingleton<IBackgroundTaskSupervisor, BackgroundTaskSupervisor>();

        services
            .AddKirihaData(PathHelper.GetDbPath())
            .AddKirihaTracking()
            .AddKirihaBackgroundServices()
            .AddKirihaUi();
    }

    private static void ConfigurePlayerServices(IServiceCollection services)
    {
        PathHelper.EnsureDirectoriesExist();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<Kiriha.Core.Abstractions.Services.ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler, Kiriha.Services.AppLifecycle.Shutdown.SettingsShutdownHandler>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<IPlayerMediaMetadataResolver, FilenamePlayerMediaMetadataResolver>();
    }
}
