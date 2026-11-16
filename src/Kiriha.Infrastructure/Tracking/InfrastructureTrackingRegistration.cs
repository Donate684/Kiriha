using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Abstractions.Extensions;
using Kiriha.Infrastructure.Tracking.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Infrastructure.Tracking;

public static class InfrastructureTrackingRegistration
{
    public static IServiceCollection AddKirihaTrackingInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<Kiriha.Core.Abstractions.Infrastructure.IAppPaths, Platform.AppPathsService>();
        services.AddSingleton<Kiriha.Core.Abstractions.Infrastructure.IBrowserLauncher, Platform.ShellBrowserLauncher>();
        services.AddSingleton<IReadOnlyList<ExternalPlayerDefinition>>(_ =>
            Kiriha.Infrastructure.Tracking.ExternalDetection.ExternalPlayerCatalog.Load());
        services.AddSingleton<DiscordService>();
        services.AddForwardedSingleton<DiscordService, IDiscordService>();
        services.AddSingleton<ExternalMediaDetectorService>();
        services.AddForwardedSingleton<ExternalMediaDetectorService, IExternalMediaDetector>();
        services.AddSingleton<InternalPlayerServer>();
        services.AddForwardedSingleton<InternalPlayerServer, IInternalPlayerServer>();
        services.AddSingleton<IStartupManager, Platform.WindowsStartupManager>();

        return services;
    }
}


