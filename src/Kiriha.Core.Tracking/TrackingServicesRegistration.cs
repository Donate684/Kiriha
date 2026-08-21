using Kiriha.Infrastructure.Extensions;
using System;
using Kiriha.Core.Shared;
using System.Net;
using System.Net.Http;
using Kiriha.Infrastructure;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Tracking;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Tracking.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Core.Tracking;

/// <summary>
/// DI registrations for the tracking layer: every service that talks to a
/// remote anime tracker (MyAnimeList, Shikimori, Jikan), the cross-tracker
/// orchestration around them (sync manager, scrobble pipeline, queues), and
/// background helpers that consume their state.
///
/// Each tracker is registered both under its concrete type (so call sites can
/// inject it directly when they need API-specific operations) and under
/// <see cref="ITrackerService"/> via a <c>sp.GetRequiredService</c> resolver
/// so <c>IEnumerable&lt;ITrackerService&gt;</c> consumers see the SAME singleton
/// instance — registering the implementation twice would create two parallel
/// instances and silently double the rate-limit budget.
/// </summary>
public static class TrackingServicesRegistration
{
    public static IServiceCollection AddKirihaTracking(this IServiceCollection services)
    {
        services.AddTransient<ResilientHttpHandler>();

        // --- MyAnimeList ---
        services.AddHttpClient("MalClient", c => { c.BaseAddress = new Uri(AppConstants.Api.Mal.BaseUrl); })
                .AddHttpMessageHandler<ResilientHttpHandler>();

        services.AddSingleton<MalAuthService>(sp =>
            new MalAuthService(sp.GetRequiredService<IHttpClientFactory>().CreateClient("MalClient")));

        services.AddSingleton<MalTokenManager>();

        services.AddSingleton<IMalApiService, MalApiService>(sp =>
            new MalApiService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("MalClient"),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<MalTokenManager>(),
                sp.GetRequiredService<JikanApiService>(),
                sp.GetRequiredService<IHttpCacheRepository>()));

        services.AddForwardedSingleton<IMalApiService, ITrackerService>();

        // --- Shikimori ---
        // No BaseAddress: ShikiApiService resolves the endpoint per-call from settings
        // (shikimori.one vs shikimori.net) and always passes absolute URLs.
        //
        // AllowAutoRedirect = false is critical: shikimori.net and shikimori.rip
        // are the same site behind two domains (regional-blocking workaround),
        // and the server geo-redirects between them. HttpClient's built-in
        // follower would (a) downgrade POST -> GET and drop the body, and
        // (b) strip the Authorization header on the cross-host hop. ShikiHttp
        // re-implements the follow with method/body/auth preserved and pins
        // the resolved host in ShikiHostResolver for the rest of the session.
        services.AddSingleton<ShikiHostResolver>();
        services.AddHttpClient("ShikiClient")
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                })
                .AddHttpMessageHandler<ResilientHttpHandler>();

        services.AddSingleton<ShikiRateLimiter>();

        services.AddSingleton<ShikiAuthService>(sp =>
            new ShikiAuthService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ShikiClient"),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ShikiHostResolver>()));

        services.AddSingleton<ShikiTokenService>();

        services.AddSingleton<IShikiApiService, ShikiApiService>(sp =>
            new ShikiApiService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ShikiClient"),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ShikiTokenService>(),
                sp.GetRequiredService<ShikiHostResolver>(),
                sp.GetRequiredService<IHttpCacheRepository>()));

        services.AddForwardedSingleton<IShikiApiService, ITrackerService>();

        // --- Jikan / AniList ---
        services.AddSingleton<JikanApiService>();
        services.AddHttpClient("AniListClient")
                .AddHttpMessageHandler<ResilientHttpHandler>();
        services.AddSingleton<AniListApiService>(sp =>
            new AniListApiService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("AniListClient"),
                sp.GetRequiredService<IHttpCacheRepository>()));
        services.AddForwardedSingleton<AniListApiService, IAniListApiService>();

        // --- RSS ---
        services.AddHttpClient("RssClient", c => c.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent));
        services.AddSingleton<NyaaFeedClient>();
        services.AddSingleton<RssFeedService>();

        // --- Cross-tracker orchestration ---
                                
                services.AddSingleton<IScrobbleService, ScrobbleService>();
        services.AddSingleton<MediaMatchingPipeline>();
        services.AddSingleton<TrackingService>();
        services.AddSingleton<AnimeSyncOrchestrator>();
        services.AddForwardedSingleton<AnimeSyncOrchestrator, IAnimeSyncOrchestrator>();
        services.AddSingleton<AnimeProgressService>();
        services.AddForwardedSingleton<AnimeProgressService, IProgressUpdateService>();
        services.AddSingleton<ISyncManager, SyncManager>();

        return services;
    }
}


