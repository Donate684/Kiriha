using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.TorrServer;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Extractors;
using Kiriha.Core.Streaming.Models;
using Kiriha.Core.Streaming.Providers;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Infrastructure.TorrServer;
using Kiriha.Mpv;
using Kiriha.Services.AppLifecycle;
using Kiriha.ViewModels.Torrents;
using Moq;
using Moq.Protected;
using Xunit;

namespace Kiriha.Tests;

public class Wave2BugfixTests
{
    // ─── Feature 8: AnimeLib Shards & Header Forwarding ───────────────────────

    [Fact]
    public void AnimeLib_SecondaryShards_UseCdnlibsDomain()
    {
        Assert.Contains("video2.cdnlibs.org", StreamingConstants.AnimeLib.VideoServerSecondary1);
        Assert.EndsWith(".%D0%B0s/", StreamingConstants.AnimeLib.VideoServerSecondary1);

        Assert.Contains("video3.cdnlibs.org", StreamingConstants.AnimeLib.VideoServerSecondary2);
        Assert.EndsWith(".%D0%B0s/", StreamingConstants.AnimeLib.VideoServerSecondary2);
    }

    [Fact]
    public void AnimeLib_RewriteLegacyDomains_RewritesLegacyHostnames()
    {
        var originalUrl1 = "https://video2.anilib.me/media/stream.m3u8";
        var rewritten1 = AnimeLibProvider.RewriteLegacyDomains(originalUrl1);
        Assert.Equal("https://video2.cdnlibs.org/media/stream.m3u8", rewritten1);

        var originalUrl2 = "https://video3.anilib.me/media/stream.m3u8";
        var rewritten2 = AnimeLibProvider.RewriteLegacyDomains(originalUrl2);
        Assert.Equal("https://video3.cdnlibs.org/media/stream.m3u8", rewritten2);

        var unrelated = "https://example.com/video.m3u8";
        Assert.Equal(unrelated, AnimeLibProvider.RewriteLegacyDomains(unrelated));
    }

    [Fact]
    public void AnimeLib_ResolveVideoServerUrl_ResolvesSecondaryEndpoints()
    {
        Assert.Equal(StreamingConstants.AnimeLib.VideoServerSecondary1, AnimeLibProvider.ResolveVideoServerUrl("secondary_1"));
        Assert.Equal(StreamingConstants.AnimeLib.VideoServerSecondary2, AnimeLibProvider.ResolveVideoServerUrl("secondary_2"));
    }

    [Fact]
    public void PlayerModeCoordinator_ExtractHeadersFromArgs_ParsesHttpHeaderArguments()
    {
        string[] args =
        [
            "--player", "stream.mp4",
            "--http-header", "Referer: https://cdnlibs.org",
            "--http-header", "User-Agent: Custom-Agent/1.0",
            "--http-header", "Origin: https://animelib.org"
        ];

        var headers = PlayerModeCoordinator.ExtractHeadersFromArgs(args);

        Assert.NotNull(headers);
        Assert.Equal(3, headers.Count);
        Assert.Equal("https://cdnlibs.org", headers["Referer"]);
        Assert.Equal("Custom-Agent/1.0", headers["User-Agent"]);
        Assert.Equal("https://animelib.org", headers["Origin"]);
    }

    [Fact]
    public void PlayerModeCoordinator_ExtractHeadersFromArgs_ReturnsNullWhenNoHeaders()
    {
        string[] args = ["--player", "stream.mp4"];
        var headers = PlayerModeCoordinator.ExtractHeadersFromArgs(args);
        Assert.Null(headers);
    }

    [Fact]
    public void MpvThumbnailer_IsValidVideoSource_AcceptsValidHttpAndHttps()
    {
        Assert.True(MpvThumbnailer.IsValidVideoSource("http://example.com/test.mp4"));
        Assert.True(MpvThumbnailer.IsValidVideoSource("https://video2.cdnlibs.org/stream.m3u8"));
        Assert.False(MpvThumbnailer.IsValidVideoSource(""));
        Assert.False(MpvThumbnailer.IsValidVideoSource(null));
        Assert.False(MpvThumbnailer.IsValidVideoSource("C:\\NonExistentFile12345.mp4"));
        Assert.False(MpvThumbnailer.IsValidVideoSource("ftp://invalid.com/file.mp4"));
    }

    // ─── Feature 9: Prevent Raw HTML Embed Fallback ───────────────────────────

    [Theory]
    [InlineData("https://example.com/stream.m3u8", true)]
    [InlineData("https://example.com/stream.m3u8?token=xyz", true)]
    [InlineData("https://example.com/video.mp4", true)]
    [InlineData("https://example.com/video.mpd", true)]
    [InlineData("https://example.com/video.mkv", true)]
    [InlineData("https://example.com/video.webm", true)]
    [InlineData("https://example.com/video.ts", true)]
    [InlineData("https://EXAMPLE.COM/VIDEO.MP4", true)]
    [InlineData("https://example.com/player.html", false)]
    [InlineData("https://example.com/embed/6BmMbB7MxWO", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TorrentsViewModel_IsDirectMediaUrl_ValidatesCorrectly(string? url, bool expected)
    {
        var result = TorrentsViewModel.IsDirectMediaUrl(url);
        Assert.Equal(expected, result);
    }

    // ─── Feature 10: Nyaa Torrent Release Parsing Heuristics ───────────────────

    private static XElement CreateNyaaItem(string title)
    {
        var nyaaNs = XNamespace.Get(AppConstants.Api.Nyaa.XmlNamespace);
        return new XElement("item",
            new XElement("title", title),
            new XElement("link", "magnet:?xt=urn:btih:0123456789abcdef"),
            new XElement(nyaaNs + "infoHash", "0123456789abcdef"),
            new XElement(nyaaNs + "size", "1.4 GiB"),
            new XElement(nyaaNs + "seeders", "42"),
            new XElement(nyaaNs + "leechers", "5"),
            new XElement(nyaaNs + "downloads", "120"),
            new XElement("pubDate", DateTime.UtcNow.ToString("R")));
    }

    [Fact]
    public void NyaaTorrentParser_SingleSequelEpisode_IsNotBatchAndRetainsEpisode()
    {
        var title = "[SubsPlease] Solo Leveling - Season 2 - 05 (1080p) [ABCD1234].mkv";
        var entity = NyaaTorrentParser.ParseItem(CreateNyaaItem(title));

        Assert.NotNull(entity);
        Assert.False(entity.IsBatch, "Single sequel episode should NOT be classified as batch");
        Assert.True(entity.Episode is "5" or "05");
        Assert.True(entity.ShowEpisodeBadge);
        Assert.Contains("Solo Leveling", entity.AnimeTitle);
    }

    [Fact]
    public void NyaaTorrentParser_SequelWithMultipleSubtitles_IsNotBatch()
    {
        var title = "[Erai-raws] Jujutsu Kaisen - 2nd Season - 14 [1080p][Multiple Subtitle].mkv";
        var entity = NyaaTorrentParser.ParseItem(CreateNyaaItem(title));

        Assert.NotNull(entity);
        Assert.False(entity.IsBatch);
        Assert.Equal("14", entity.Episode);
        Assert.True(entity.ShowEpisodeBadge);
    }

    [Fact]
    public void NyaaTorrentParser_ExplicitBatchKeyword_IsBatch()
    {
        var title = "[SubsPlease] Solo Leveling - Season 2 (01-12) (1080p) [Batch]";
        var entity = NyaaTorrentParser.ParseItem(CreateNyaaItem(title));

        Assert.NotNull(entity);
        Assert.True(entity.IsBatch);
    }

    [Fact]
    public void NyaaTorrentParser_CompleteRelease_IsBatch()
    {
        var title = "[HorribleSubs] Fairy Tail - Complete [720p]";
        var entity = NyaaTorrentParser.ParseItem(CreateNyaaItem(title));

        Assert.NotNull(entity);
        Assert.True(entity.IsBatch);
    }

    [Fact]
    public void NyaaTorrentParser_StandaloneMovieWithoutEpisode_IsNotBatch()
    {
        var title = "[SubsPlease] Demon Slayer - Mugen Train - Movie (1080p)";
        var entity = NyaaTorrentParser.ParseItem(CreateNyaaItem(title));

        Assert.NotNull(entity);
        Assert.False(entity.IsBatch);
    }

    [Fact]
    public void NyaaTorrentParser_StandaloneSpecialWithoutEpisode_IsNotBatch()
    {
        var title = "[SubsPlease] Attack on Titan - Special (1080p)";
        var entity = NyaaTorrentParser.ParseItem(CreateNyaaItem(title));

        Assert.NotNull(entity);
        Assert.False(entity.IsBatch);
    }

    // ─── Feature 11: Streaming Extractors & Providers Hardening ───────────────

    [Fact]
    public void StreamingConstants_Timeouts_ConfiguredProperly()
    {
        Assert.Equal(8, StreamingConstants.Timeouts.ProviderSearchTimeoutSeconds);
        Assert.Equal(2, StreamingConstants.Timeouts.MirrorProbeTimeoutSeconds);
    }

    [Theory]
    [InlineData("//kodik.info/stream.m3u8", "https://kodik.info/stream.m3u8")]
    [InlineData("http://kodik.info/stream.m3u8", "http://kodik.info/stream.m3u8")]
    [InlineData("https://kodik.info/stream.m3u8", "https://kodik.info/stream.m3u8")]
    [InlineData("kodik.info/stream.m3u8", "https://kodik.info/stream.m3u8")]
    public void KodikVideoExtractor_NormalizeScheme_NormalizesUrlsCorrectly(string input, string expected)
    {
        var result = KodikVideoExtractor.NormalizeScheme(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void KodikVideoExtractor_DecodeUrl_DirectM3u8WithQueryParams_NotRot18Decrypted()
    {
        var directUrl = "https://example.com/playlist.m3u8?query=test&token=abc";
        var decoded = KodikVideoExtractor.DecodeUrl(directUrl);
        Assert.Equal(directUrl, decoded);
    }

    [Fact]
    public void AniboomVideoExtractor_DataParametersRegex_MatchesSingleAndDoubleQuotedAttributes()
    {
        var regex = AniboomVideoExtractor.GenerateDataParametersRegex();

        var singleQuotedHtml = @"<div id=""player"" data-parameters='{""hls"":""{\""src\"":\""https://aniboom.one/video.m3u8\""}""}'></div>";
        var singleMatch = regex.Match(singleQuotedHtml);
        Assert.True(singleMatch.Success);
        var singleGroup = singleMatch.Groups[1].Success ? singleMatch.Groups[1].Value : singleMatch.Groups[2].Value;
        Assert.Contains("video.m3u8", singleGroup);

        var doubleQuotedHtml = @"<div id=""player"" data-parameters=""{&quot;hls&quot;:&quot;https://aniboom.one/video.m3u8&quot;}""></div>";
        var doubleMatch = regex.Match(doubleQuotedHtml);
        Assert.True(doubleMatch.Success);
    }

    // ─── Feature 12: TorrServer Ping & Port Sync ──────────────────────────────

    [Fact]
    public async Task TorrServerService_PingServerAsync_ValidatesMatrixBanner()
    {
        var mockSettings = new Mock<Kiriha.Core.Abstractions.Services.ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        // 1. Handler returning OK without Matrix
        var mockHandlerInvalid = new Mock<HttpMessageHandler>();
        mockHandlerInvalid.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("OK Server")
            });

        using var clientInvalid = new HttpClient(mockHandlerInvalid.Object);
        var serviceInvalid = new TorrServerService(mockSettings.Object, clientInvalid);
        var isAliveInvalid = await serviceInvalid.PingServerAsync(CancellationToken.None);
        Assert.False(isAliveInvalid, "TorrServer ping must return false if response does not contain 'MatriX'");

        // 2. Handler returning MatriX banner
        var mockHandlerValid = new Mock<HttpMessageHandler>();
        mockHandlerValid.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("TorrServer MatriX.132")
            });

        using var clientValid = new HttpClient(mockHandlerValid.Object);
        var serviceValid = new TorrServerService(mockSettings.Object, clientValid);
        var isAliveValid = await serviceValid.PingServerAsync(CancellationToken.None);
        Assert.True(isAliveValid, "TorrServer ping must return true when response contains 'MatriX'");
    }

    [Fact]
    public void TorrServerService_ServerPort_SynchronizesServerUrl()
    {
        var mockSettings = new Mock<Kiriha.Core.Abstractions.Services.ISettingsService>();
        var settings = new AppSettings
        {
            Torrents = new()
            {
                Streaming = new()
                {
                    ServerUrl = "http://127.0.0.1:8090",
                    ServerPort = 9090
                }
            }
        };
        mockSettings.Setup(s => s.Current).Returns(settings);

        using var client = new HttpClient();
        var service = new TorrServerService(mockSettings.Object, client);

        Assert.Equal("http://127.0.0.1:9090", service.ServerUrl);
    }

    // ─── Feature 13: Concurrency, Relevance Scorer, Direct MP4 ────────────────

    [Fact]
    public void OnlineRelevanceScorer_SequelMismatchPenalty_IdentifiesUnpromptedSequels()
    {
        var hasUnprompted = OnlineRelevanceScorer.HasUnpromptedSequelTokens("Solo Leveling", "Solo Leveling Season 2");
        Assert.True(hasUnprompted);

        var score = OnlineRelevanceScorer.ComputeRelevance("Solo Leveling", "Solo Leveling Season 2");
        Assert.True(score <= 0.50, $"Unprompted sequel match score should be capped at 0.50, got {score}");

        var promptedHasUnprompted = OnlineRelevanceScorer.HasUnpromptedSequelTokens("Solo Leveling Season 2", "Solo Leveling Season 2");
        Assert.False(promptedHasUnprompted);

        var promptedScore = OnlineRelevanceScorer.ComputeRelevance("Solo Leveling Season 2", "Solo Leveling Season 2");
        Assert.Equal(1.0, promptedScore);
    }

    [Fact]
    public void OnlineSourceVideo_IsDirectMp4_ValidatesDirectMp4Url()
    {
        var mp4Item = new OnlineSourceItem(
            new StreamingSource
            {
                Provider = "Other",
                PlayerUrl = "https://example.com/video.mp4",
                Title = "Sample Title"
            });
        Assert.True(mp4Item.IsDirectMp4);

        var m3u8Item = new OnlineSourceItem(
            new StreamingSource
            {
                Provider = "Other",
                PlayerUrl = "https://example.com/video.m3u8",
                Title = "Sample Title"
            });
        Assert.False(m3u8Item.IsDirectMp4);
    }
}
