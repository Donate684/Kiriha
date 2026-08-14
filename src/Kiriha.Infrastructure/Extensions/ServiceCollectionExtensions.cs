using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton for <typeparamref name="TImpl"/> and then forwards
    /// <typeparamref name="TIface"/> to the same instance.
    /// </summary>
    public static IServiceCollection AddForwardedSingleton<TImpl, TIface>(this IServiceCollection services)
        where TImpl : class, TIface
        where TIface : class
    {
        services.AddSingleton<TIface>(sp => sp.GetRequiredService<TImpl>());
        return services;
    }
}


