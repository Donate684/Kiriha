using System.Net;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Kiriha.Core.Streaming.Providers;
using Kiriha.Core.Streaming.Services;
using Moq;

namespace Kiriha.Tests;

public sealed class OnlineStreamingResilienceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Search_PreservesHealthyProvidersWhenAnotherFailsOrDoesNotRespond()
    {
        using var client = new HttpClient(new FixtureHandler(uri => uri.AbsolutePath == new Uri(StreamingConstants.AniLibria.SearchEndpoint).AbsolutePath
            ? Reply(HttpStatusCode.OK, "<html>Upstream error</html>")
            : Reply(HttpStatusCode.OK, """
                {"data":[null, {"id":14,"name":{"main":"Frieren"},"poster":false},
                         {"name":{"main":"Missing identifier"}}, {"id":15,"name":42}]}
                """)));
        var stalled = new TaskCompletionSource<IReadOnlyList<StreamingSearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unavailable = new Mock<IStreamingProvider>();
        unavailable.SetupGet(p => p.ProviderName).Returns("Unavailable");
        unavailable.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Unavailable"));
        var slow = new Mock<IStreamingProvider>();
        slow.SetupGet(p => p.ProviderName).Returns("Slow");
        slow.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(stalled.Task);
        var service = new StreamingService([unavailable.Object, slow.Object, new AniLibriaProvider(client)], [],
            Deadline, TestTimeout);

        try
        {
            var result = Assert.Single(await service.SearchAllAsync("Frieren").WaitAsync(TestTimeout));
            Assert.Equal("14", result.Id);
            Assert.Equal("Frieren", result.Title);
            Assert.False(stalled.Task.IsCompleted);
        }
        finally
        {
            stalled.TrySetResult([]);
        }
    }

    [Fact]
    public async Task Search_DisposingEnumeratorCancelsTheRemainingProvider()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var quick = new Mock<IStreamingProvider>();
        quick.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StreamingSearchResult { Id = "1", Title = "First", Url = "/1", ProviderName = "fixture" }]);
        var waiting = new Mock<IStreamingProvider>();
        waiting.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) =>
            {
                using var registration = ct.Register(() => cancelled.TrySetResult());
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return [];
            });
        var service = new StreamingService([quick.Object, waiting.Object], []);

        await using (var enumerator = service.SearchStreamAsync("Example").GetAsyncEnumerator())
            Assert.True(await enumerator.MoveNextAsync());
        await cancelled.Task.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task AniLibria_DetailsRetainValidEpisodesAndQualitiesInMixedResponse()
    {
        using var client = new HttpClient(new FixtureHandler(_ => Reply(HttpStatusCode.OK, """
            {
                "id":14, "poster":[], "episodes":[
                    null, {"id":"episode-2","ordinal":2,"name":false,
                           "hls_1080":{"url":"bad"},"hls_720":"//cdn.example.org/ep2.m3u8?sig=value","hls_480":17},
                    {"id":"episode-3","ordinal":3,"hls_1080":"https://cdn.example.org/ep3.m3u8"}
                ]
            }
            """)));
        var service = new StreamingService([new AniLibriaProvider(client)], []);
        var details = Assert.IsType<StreamingAnimeDetails>(
            await service.GetAnimeDetailsAsync(StreamingConstants.Providers.AniLibria, "14"));
        Assert.Equal(new[] { 2, 3 }, details.Episodes.Select(ep => ep.EpisodeNumber));
        var secondEpisode = Assert.Single(details.Episodes[0].Sources);
        Assert.Equal("https://cdn.example.org/ep2.m3u8?sig=value", secondEpisode.PlayerUrl);
        Assert.Single(details.Episodes[1].Sources);
    }

    [Fact]
    public async Task ResolveStream_SkipsTimedOutAndUnplayableExtractorsWithoutLosingStreamMetadata()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<StreamingVideo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var streams = new[]
        {
            new StreamingVideo { Url = "https://player.example.org/embed", VideoType = "embed", Quality = 1080 },
            new StreamingVideo { Url = "file:///video.mp4", VideoType = "mp4", Quality = 1080 }
        };
        var usable = new StreamingVideo
        {
            Url = "//cdn.example.org/video.mpd?sig=value", VideoType = "mpd", Quality = 720,
            SubtitleUrl = "https://cdn.example.org/subtitles.vtt",
            Headers = new Dictionary<string, string> { ["Referer"] = "https://player.example.org/" }
        };
        var service = new StreamingService([], [Extractor(pending.Task), Extractor(Task.FromResult<IReadOnlyList<StreamingVideo>>(streams)),
            Extractor(Task.FromResult<IReadOnlyList<StreamingVideo>>([usable]))], TestTimeout, Deadline);
        try
        {
            var actual = Assert.Single(await service.ResolveStreamAsync("https://player.example.org/episode").WaitAsync(TestTimeout));
            Assert.Equal(usable with { Url = "https:" + usable.Url }, actual);
            Assert.Empty(await service.ResolveStreamAsync("file:///video.mp4"));
        }
        finally
        {
            pending.TrySetResult([]);
        }
    }

    [Fact]
    public async Task Anime365_ReleasePageIsNotTreatedAsADirectVideo()
    {
        using var client = new HttpClient(new FixtureHandler(_ => throw new InvalidOperationException("Unexpected request")));
        var service = new StreamingService([], [new Kiriha.Core.Streaming.Extractors.Anime365Extractor(client)]);

        Assert.Empty(await service.ResolveStreamAsync("https://smotret-anime.co/catalog/example"));
        var direct = Assert.Single(await service.ResolveStreamAsync("https://smotret-anime.co/media/example.mp4"));
        Assert.Equal("mp4", direct.VideoType);
    }

    [Fact]
    public async Task Anime365_EmptyTranslationsDoNotQueryAnUnrelatedSeriesWithTheSameId()
    {
        var paths = new List<string>();
        using var client = new HttpClient(new FixtureHandler(uri =>
        {
            paths.Add(uri.AbsolutePath);
            return Reply(HttpStatusCode.OK, """{"data":[]}""");
        }));
        var provider = new Anime365Provider(client, new Anime365HostResolver());

        Assert.Empty(await provider.GetEpisodeSourcesAsync("123", 1));
        var path = Assert.Single(paths);
        Assert.EndsWith("/translations", path, StringComparison.Ordinal);
    }

    private static IVideoExtractor Extractor(Task<IReadOnlyList<StreamingVideo>> result)
    {
        var extractor = new Mock<IVideoExtractor>();
        extractor.Setup(e => e.CanExtract(It.IsAny<string>())).Returns(true);
        extractor.Setup(e => e.ExtractVideosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(result);
        return extractor.Object;
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    private sealed class FixtureHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(respond(Assert.IsType<Uri>(request.RequestUri)));
        }
    }
}
