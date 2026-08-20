using Kiriha.Core.Abstractions.Services;
using Kiriha.Infrastructure.Extensions;
using Kiriha.Infrastructure.Tracking.Integration;
using Microsoft.Extensions.DependencyInjection;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Infrastructure.Tracking;

public static class InfrastructureTrackingRegistration
{
    public static IServiceCollection AddKirihaTrackingInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<System.Collections.Generic.IReadOnlyList<AnisthesiaPlayer>>(sp => Kiriha.Infrastructure.Tracking.Anisthesia.AnisthesiaPlayerLoader.Load());
        services.AddSingleton<SmtcService>();
        services.AddSingleton<DiscordService>();
        services.AddForwardedSingleton<DiscordService, IDiscordService>();
        services.AddSingleton<AnisthesiaService>();
        services.AddForwardedSingleton<AnisthesiaService, IExternalMediaDetector>();
        services.AddSingleton<InternalPlayerServer>();
        services.AddForwardedSingleton<InternalPlayerServer, IInternalPlayerServer>();

        return services;
    }
}


