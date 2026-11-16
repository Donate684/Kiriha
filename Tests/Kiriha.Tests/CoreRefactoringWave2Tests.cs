using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kiriha.Core.Abstractions.Extensions;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Collections;
using Kiriha.Core.Domain.Utils.Classification;
using Kiriha.Core.Domain.Utils.Parsing;
using Kiriha.Core.Streaming;
using Kiriha.Core.Streaming.Services;
using Kiriha.Core.Tracking;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Library;
using Kiriha.Data;
using Kiriha.Data.Core;
using Kiriha.Infrastructure.Caching;
using Kiriha.Infrastructure.Settings;
using Kiriha.Mpv.UI.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Kiriha.Tests;

public sealed class CoreRefactoringWave2Tests
{
    [Fact]
    public void KirihaCoreTracking_DoesNotReference_Infrastructure_Or_Data()
    {
        var trackingAssembly = typeof(TrackingServicesRegistration).Assembly;
        var referencedNames = trackingAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Kiriha.Infrastructure", referencedNames);
        Assert.DoesNotContain("Kiriha.Data", referencedNames);
        Assert.DoesNotContain("Kiriha", referencedNames);
    }

    [Fact]
    public void EvictedCoreComponents_ResideInProperTargetAssemblies()
    {
        var uiAssembly = typeof(Kiriha.App).Assembly;

        Assert.NotEqual(uiAssembly, typeof(AnimeRepository).Assembly);
        Assert.Equal("Kiriha.Core.Tracking", typeof(AnimeRepository).Assembly.GetName().Name);

        Assert.Equal("Kiriha.Core.Domain", typeof(AnimeFilterEngine).Assembly.GetName().Name);
        Assert.Equal("Kiriha.Core.Domain", typeof(AnimeCollectionProjection).Assembly.GetName().Name);
        Assert.Equal("Kiriha.Core.Domain", typeof(DonghuaDetector).Assembly.GetName().Name);
        Assert.Equal("Kiriha.Core.Domain", typeof(BearerTokenHelper).Assembly.GetName().Name);

        Assert.Equal("Kiriha.Infrastructure", typeof(SettingsService).Assembly.GetName().Name);
        Assert.Equal("Kiriha.Infrastructure", typeof(SeasonalCacheStore).Assembly.GetName().Name);
        Assert.Equal("Kiriha.Mpv.UI", typeof(PlayerMediaMetadataResolver).Assembly.GetName().Name);
        Assert.Equal("Kiriha.Core.Tracking", typeof(HttpConditionalCache).Assembly.GetName().Name);
        Assert.Equal("Kiriha.Data", typeof(DataLayerRegistration).Assembly.GetName().Name);
        Assert.Equal("Kiriha.Core.Streaming", typeof(Anime365AuthService).Assembly.GetName().Name);
    }

    [Fact]
    public void NonUiAssemblies_PublicTypesMatchAssemblyRootNamespace()
    {
        Assembly[] assemblies =
        [
            typeof(ServiceCollectionExtensions).Assembly,
            typeof(DataLayerRegistration).Assembly,
            typeof(TrackingServicesRegistration).Assembly,
            typeof(StreamingServicesRegistration).Assembly,
            typeof(SettingsService).Assembly,
            typeof(PlayerMediaMetadataResolver).Assembly,
        ];

        foreach (var assembly in assemblies)
        {
            var rootName = assembly.GetName().Name!;
            foreach (var type in assembly.GetExportedTypes())
            {
                Assert.NotNull(type.Namespace);
                Assert.True(
                    type.Namespace!.Equals(rootName, StringComparison.Ordinal) ||
                    type.Namespace.StartsWith(rootName + ".", StringComparison.Ordinal),
                    $"Type '{type.FullName}' in assembly '{rootName}' violates root namespace convention.");
            }
        }
    }

    [Fact]
    public async Task Anime365AuthService_LoginAndAccountStatus_HandlesSuccessAndErrors()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            var query = req.RequestUri?.Query ?? string.Empty;

            if (path.EndsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (query.Contains("bad%40mail.test", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""{"error":{"code":401,"message":"Invalid password"}}""");
                }

                if (query.Contains("empty%40mail.test", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""{"data":{}}""");
                }

                return JsonResponse("""{"data":{"access_token":"tok_abc123"}}""");
            }

            if (path.EndsWith("/me", StringComparison.OrdinalIgnoreCase))
            {
                if (query.Contains("tok_abc123", StringComparison.Ordinal))
                {
                    return JsonResponse("""{"data":{"isLogined":true,"name":"KirihaTester","isPremium":true,"premiumUntil":"2027-01-01"}}""");
                }

                return JsonResponse("""{"data":{"isLogined":false}}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var authService = new Anime365AuthService(httpClient);

        var ok = await authService.LoginAsync("user@mail.test", "secret");
        Assert.True(ok.IsSuccess);
        Assert.Equal("tok_abc123", ok.AccessToken);

        var bad = await authService.LoginAsync("bad@mail.test", "wrong");
        Assert.False(bad.IsSuccess);
        Assert.Equal("Invalid password", bad.ErrorMessage);

        var missing = await authService.LoginAsync("empty@mail.test", "secret");
        Assert.False(missing.IsSuccess);
        Assert.True(missing.TokenMissing);

        var status = await authService.GetAccountStatusAsync("tok_abc123");
        Assert.NotNull(status);
        Assert.True(status!.IsLoggedIn);
        Assert.Equal("KirihaTester", status.Username);
        Assert.True(status.IsPremium);
        Assert.Equal("2027-01-01", status.PremiumUntil);

        var loggedOut = await authService.GetAccountStatusAsync("expired_tok");
        Assert.NotNull(loggedOut);
        Assert.False(loggedOut!.IsLoggedIn);
    }

    [Fact]
    public void BearerTokenHelper_CleansHeaderAndExtractsJwtPayloadName()
    {
        Assert.Equal("my-raw-token", BearerTokenHelper.CleanToken("  Bearer \"my-raw-token\" \r\n"));

        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"HS256"}""")).TrimEnd('=');
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { username = "Otaku42" }))).TrimEnd('=');
        var fakeJwt = $"{header}.{payload}.sig";

        Assert.Equal("Otaku42", BearerTokenHelper.TryExtractUsername(fakeJwt));
    }

    [Fact]
    public void DataLayerRegistration_ValidatesAndResolvesPersistenceGraph()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"Kiriha_Wave2_DI_{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IHistoryService>());
            services.AddKirihaPersistence(dbPath);

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            Assert.NotNull(provider.GetRequiredService<IDatabaseInitializer>());
            Assert.NotNull(provider.GetRequiredService<IUserAnimeRepository>());
            Assert.NotNull(provider.GetRequiredService<IMetadataRepository>());
            Assert.NotNull(provider.GetRequiredService<IHistoryRepository>());
            Assert.NotNull(provider.GetRequiredService<ISyncTaskRepository>());
            Assert.NotNull(provider.GetRequiredService<CacheCleanupService>());
        }
        finally
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
