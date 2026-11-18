using System.Net;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Moq;

namespace Kiriha.Tests;

public sealed class MalTrackerServiceTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
        public string ResponseContent { get; set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(ResponseStatus)
            {
                Content = new StringContent(ResponseContent)
            };
        }
    }

    private static (MalApiService service, CaptureHandler handler) CreateService(bool authenticated = true)
    {
        var handler = new CaptureHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri(AppConstants.Api.Mal.BaseUrl) };

        var settings = new AppSettings();
        if (authenticated)
        {
            settings.Api.Mal = new MalTokens
            {
                AccessToken = "test-token",
                RefreshToken = "test-refresh",
                ExpiresIn = 3600,
                CreatedAt = DateTime.UtcNow
            };
        }

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var authService = new MalAuthService(client);
        var tokenManager = new MalTokenManager(mockSettings.Object, authService);
        var mockCache = new Mock<IHttpCacheRepository>();

        var apiService = new MalApiService(client, mockSettings.Object, tokenManager, mockCache.Object);
        return (apiService, handler);
    }

    [Fact]
    public async Task SaveFullListStatusAsync_OmitsEmptyDates_AndUsesCommentsParameter()
    {
        var (service, handler) = CreateService();

        var item = new AnimeEntity
        {
            Id = 10580,
            MediaKind = MediaKind.Anime,
            Progress = 1,
            Status = UserAnimeStatus.Completed,
            Score = "8",
            Notes = "Loved the 3D OVA!",
            DateStarted = null, // Should NOT send start_date
            DateCompleted = new DateTime(2026, 11, 18)
        };

        var outcome = await service.SaveFullListStatusAsync(item);

        Assert.Equal(SyncOutcome.Success, outcome);
        Assert.NotNull(handler.LastRequestBody);

        var body = WebUtility.UrlDecode(handler.LastRequestBody!);

        Assert.Contains("num_watched_episodes=1", body);
        Assert.Contains("status=completed", body);
        Assert.Contains("score=8", body);
        Assert.Contains("comments=Loved the 3D OVA!", body);
        Assert.Contains("finish_date=2026-11-18", body);
        Assert.DoesNotContain("start_date", body);
        Assert.DoesNotContain("notes=", body);
    }

    [Fact]
    public async Task SaveFullListStatusAsync_ForManga_UsesMangaSpecificParameters()
    {
        var (service, handler) = CreateService();

        var item = new AnimeEntity
        {
            Id = 54321,
            MediaKind = MediaKind.Manga,
            ChaptersRead = 45,
            VolumesRead = 5,
            Status = UserAnimeStatus.Watching, // Maps to reading
            Score = "0", // Unrated - should omit score
            IsRewatching = true,
            RewatchCount = 2
        };

        var outcome = await service.SaveFullListStatusAsync(item);

        Assert.Equal(SyncOutcome.Success, outcome);
        Assert.NotNull(handler.LastRequestBody);

        var body = WebUtility.UrlDecode(handler.LastRequestBody!);

        Assert.Contains("num_chapters_read=45", body);
        Assert.Contains("num_volumes_read=5", body);
        Assert.Contains("status=reading", body);
        Assert.Contains("is_rereading=true", body);
        Assert.Contains("num_times_reread=2", body);
        Assert.DoesNotContain("is_rewatching", body);
        Assert.DoesNotContain("num_times_rewatched", body);
        Assert.Contains("score=0", body);
        Assert.DoesNotContain("start_date", body);
        Assert.DoesNotContain("finish_date", body);
    }

    [Fact]
    public async Task UpdateProgressAsync_OnlySendsProvidedParameters()
    {
        var (service, handler) = CreateService();

        var outcome = await service.UpdateProgressAsync(10580, 5, UserAnimeStatus.Watching);

        Assert.Equal(SyncOutcome.Success, outcome);
        Assert.NotNull(handler.LastRequestBody);

        var body = Uri.UnescapeDataString(handler.LastRequestBody!);

        Assert.Contains("num_watched_episodes=5", body);
        Assert.Contains("status=watching", body);
        Assert.DoesNotContain("score", body);
        Assert.DoesNotContain("is_rewatching", body);
    }

    [Fact]
    public async Task MapStatusToOutcome_Treats403AsTransientFailure()
    {
        var (service, handler) = CreateService();
        handler.ResponseStatus = HttpStatusCode.Forbidden;

        var outcome = await service.UpdateProgressAsync(10580, 1);

        Assert.Equal(SyncOutcome.TransientFailure, outcome);
    }

    [Fact]
    public async Task MapStatusToOutcome_Treats401AsTransient_WhenCredentialsPresent()
    {
        var (service, handler) = CreateService(authenticated: true);
        handler.ResponseStatus = HttpStatusCode.Unauthorized;

        var outcome = await service.UpdateProgressAsync(10580, 1);

        // When 401 returns and credentials exist, it tries refresh and if still 401,
        // treats as TransientFailure to allow SyncManager to retry instead of permanent failure
        Assert.Equal(SyncOutcome.TransientFailure, outcome);
    }
}
