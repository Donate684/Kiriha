using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Extractors;
using Kiriha.Core.Streaming.Providers;
using Kiriha.Core.Streaming.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Core.Streaming;

/// <summary>
/// Dependency Injection registrations for online anime streaming providers and video extractors.
/// </summary>
public static class StreamingServicesRegistration
{
    public static IServiceCollection AddKirihaStreaming(this IServiceCollection services)
    {
        services.AddHttpClient("StreamingClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(StreamingConstants.Timeouts.HttpClientTimeoutSeconds);
            client.DefaultRequestHeaders.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation(StreamingConstants.Headers.AcceptLanguage, StreamingConstants.Headers.RuRuLanguage);
        });

        // Host resolvers & mirror rewriters
        services.AddSingleton<Anime365HostResolver>();
        services.AddSingleton<Kiriha.Core.Abstractions.Services.IImageUrlRewriter>(sp => sp.GetRequiredService<Anime365HostResolver>());

        // Extractors
        services.AddSingleton<IVideoExtractor>(sp =>
            new KodikVideoExtractor(sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient")));

        services.AddSingleton<IVideoExtractor>(sp =>
            new AniboomVideoExtractor(sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient")));

        services.AddSingleton<IVideoExtractor>(sp =>
            new SibnetVideoExtractor(sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient")));

        services.AddSingleton<IVideoExtractor>(sp =>
            new Anime365Extractor(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient"),
                sp.GetService<Kiriha.Core.Abstractions.Services.ISettingsService>(),
                sp.GetRequiredService<Anime365HostResolver>()));

        // Providers
        services.AddSingleton<IStreamingProvider>(sp =>
            new AniLibriaProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient")));

        services.AddSingleton<IStreamingProvider>(sp =>
            new AnimeLibProvider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient"),
                sp.GetService<Kiriha.Core.Abstractions.Services.ISettingsService>()));

        services.AddSingleton<IStreamingProvider>(sp =>
            new DreamCastProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient")));

        services.AddSingleton<IStreamingProvider>(sp =>
            new YummyAnimeProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient")));

        services.AddSingleton<IStreamingProvider>(sp =>
            new Anime365Provider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("StreamingClient"),
                sp.GetRequiredService<Anime365HostResolver>(),
                sp.GetService<Kiriha.Core.Abstractions.Services.ISettingsService>()));

        // Unified service
        services.AddSingleton<IStreamingService, StreamingService>();

        return services;
    }
}
