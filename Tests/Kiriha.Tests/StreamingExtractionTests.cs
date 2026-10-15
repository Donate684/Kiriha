using System.Text;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Extractors;
using Kiriha.Core.Streaming.Models;
using Kiriha.Core.Streaming.Providers;
using Kiriha.Core.Streaming.Services;
using Kiriha.ViewModels.Torrents;
using Moq;

namespace Kiriha.Tests;

public class StreamingExtractionTests
{
    [Fact]
    public void Kodik_CanExtract_ValidUrls_ReturnsTrue()
    {
        using var client = new HttpClient();
        var extractor = new KodikVideoExtractor(client);

        Assert.True(extractor.CanExtract("https://kodik.info/serial/64218/890744b309ec026d43742995d0d49cd7/720p"));
        Assert.True(extractor.CanExtract("https://aniqit.com/video/72755/dc966c03a7cb719dac577d8004a9b091/720p"));
        Assert.True(extractor.CanExtract("https://kodik.info/seria/1133512/04d5f7824ba3563bd78e44a22451bb45/720p"));
        Assert.False(extractor.CanExtract("https://aniboom.one/embed/6BmMbB7MxWO"));
        Assert.False(extractor.CanExtract("https://google.com"));
    }

    [Fact]
    public void Kodik_DecryptRot18_TransformsCorrectly()
    {
        // ROT-18 is reversible (18 + 8 = 26)
        var original = "Hello World";
        var encrypted = KodikVideoExtractor.DecryptRot18(original);
        Assert.NotEqual(original, encrypted);

        // Verify known mapping: 'A' -> 'S' (ord('A') - 65 + 18) % 26 = 18 ('S')
        Assert.Equal("S", KodikVideoExtractor.DecryptRot18("A"));
        Assert.Equal("s", KodikVideoExtractor.DecryptRot18("a"));
    }

    [Fact]
    public void Kodik_DecodeUrl_DirectM3u8_ReturnsHttpsUrl()
    {
        var rawM3u8 = "//cloud.kodik.info/123/video.m3u8";
        var decoded = KodikVideoExtractor.DecodeUrl(rawM3u8);
        Assert.Equal("https://cloud.kodik.info/123/video.m3u8", decoded);
    }

    [Fact]
    public void Kodik_DecodeUrl_Rot18Base64_DecodesSuccessfully()
    {
        var targetUrl = "https://cloud.kodik.info/video.m3u8";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(targetUrl));

        // To encrypt with ROT-18, we can apply ROT-8 ((26 - 18) % 26 = 8)
        var sb = new StringBuilder(base64.Length);
        foreach (var ch in base64)
        {
            if (char.IsAsciiLetterUpper(ch))
                sb.Append((char)((ch - 'A' + 8) % 26 + 'A'));
            else if (char.IsAsciiLetterLower(ch))
                sb.Append((char)((ch - 'a' + 8) % 26 + 'a'));
            else
                sb.Append(ch);
        }
        var encoded = sb.ToString();

        var decoded = KodikVideoExtractor.DecodeUrl(encoded);
        Assert.Equal(targetUrl, decoded);
    }

    [Fact]
    public void Aniboom_CanExtract_ValidUrls_ReturnsTrue()
    {
        using var client = new HttpClient();
        var extractor = new AniboomVideoExtractor(client);

        Assert.True(extractor.CanExtract("https://aniboom.one/embed/6BmMbB7MxWO?episode=1&translation=30"));
        Assert.True(extractor.CanExtract("https://www.aniboom.one/video/123"));
        Assert.False(extractor.CanExtract("https://kodik.info/serial/123"));
    }

    [Fact]
    public void Aniboom_ParseStreamsFromHtml_ValidDataParameters_ExtractsStreams()
    {
        var sampleHtml = """
            <!DOCTYPE html>
            <html>
            <body>
            <div id="video" data-parameters="{&quot;hls&quot;:&quot;{\&quot;src\&quot;:\&quot;https:\/\/stream.aniboom.one\/master.m3u8\&quot;,\&quot;type\&quot;:\&quot;application\/x-mpegURL\&quot;}&quot;,&quot;dash&quot;:&quot;{\&quot;src\&quot;:\&quot;https:\/\/stream.aniboom.one\/master.mpd\&quot;,\&quot;type\&quot;:\&quot;application\/dash+xml\&quot;}&quot;}"></div>
            </body>
            </html>
            """;

        var streams = AniboomVideoExtractor.ParseStreamsFromHtml(sampleHtml);

        Assert.Equal(2, streams.Count);
        var hls = streams.FirstOrDefault(s => s.VideoType == "m3u8");
        var dash = streams.FirstOrDefault(s => s.VideoType == "mpd");

        Assert.NotNull(hls);
        Assert.Equal("https://stream.aniboom.one/master.m3u8", hls.Url);
        Assert.Equal(1080, hls.Quality);
        Assert.True(hls.Headers.ContainsKey("Referer"));
        Assert.Equal("https://aniboom.one/", hls.Headers["Referer"]);

        Assert.NotNull(dash);
        Assert.Equal("https://stream.aniboom.one/master.mpd", dash.Url);
        Assert.Equal(1080, dash.Quality);
    }

    [Fact]
    public void Sibnet_CanExtract_ValidUrls_ReturnsTrue()
    {
        using var client = new HttpClient();
        var extractor = new SibnetVideoExtractor(client);

        Assert.True(extractor.CanExtract("https://video.sibnet.ru/shell.php?videoid=1234567"));
        Assert.True(extractor.CanExtract("https://www.video.sibnet.ru/v/123"));
        Assert.False(extractor.CanExtract("https://kodik.info/123"));
    }

    [Fact]
    public void Sibnet_ParseStreamsFromHtml_ExtractsMp4Url()
    {
        var sampleHtml = """
            <div class="video-player">
                <video src="/v/1234567.mp4"></video>
            </div>
            """;

        var playerUrl = "https://video.sibnet.ru/shell.php?videoid=1234567";
        var streams = SibnetVideoExtractor.ParseStreamsFromHtml(sampleHtml, playerUrl);

        Assert.Single(streams);
        var video = streams[0];
        Assert.Equal("https://video.sibnet.ru/v/1234567.mp4", video.Url);
        Assert.Equal("mp4", video.VideoType);
        Assert.Equal(480, video.Quality);
        Assert.Equal(playerUrl, video.Headers["Referer"]);
    }

    [Fact]
    public async Task StreamingService_DirectM3u8Url_ResolvesWithoutExtractor()
    {
        var service = new StreamingService([], []);
        var directUrl = "https://cdn.example.com/stream.m3u8";

        var streams = await service.ResolveStreamAsync(directUrl);

        Assert.Single(streams);
        Assert.Equal(directUrl, streams[0].Url);
        Assert.Equal("m3u8", streams[0].VideoType);
        Assert.Equal(1080, streams[0].Quality);
    }

    [Fact]
    public async Task StreamingService_MatchingExtractor_ResolvesVideo()
    {
        var mockExtractor = new Mock<IVideoExtractor>();
        mockExtractor.Setup(e => e.CanExtract(It.IsAny<string>())).Returns(true);
        mockExtractor.Setup(e => e.ExtractVideosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StreamingVideo
                {
                    Url = "https://stream.com/video.m3u8",
                    Quality = 720,
                    VideoType = "m3u8"
                }
            ]);

        var service = new StreamingService([], [mockExtractor.Object]);
        var streams = await service.ResolveStreamAsync("https://custom-player.com/123");

        Assert.Single(streams);
        Assert.Equal("https://stream.com/video.m3u8", streams[0].Url);
        Assert.Equal(720, streams[0].Quality);
    }

    [Fact]
    public async Task StreamingService_SearchAll_AggregatesResults()
    {
        var mockProvider1 = new Mock<IStreamingProvider>();
        mockProvider1.SetupGet(p => p.ProviderName).Returns("Provider1");
        mockProvider1.Setup(p => p.SearchAsync("Frieren", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StreamingSearchResult
                {
                    Id = "1",
                    Title = "Frieren P1",
                    Url = "/anime/1",
                    ProviderName = "Provider1"
                }
            ]);

        var mockProvider2 = new Mock<IStreamingProvider>();
        mockProvider2.SetupGet(p => p.ProviderName).Returns("Provider2");
        mockProvider2.Setup(p => p.SearchAsync("Frieren", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StreamingSearchResult
                {
                    Id = "2",
                    Title = "Frieren P2",
                    Url = "/anime/2",
                    ProviderName = "Provider2"
                }
            ]);

        var service = new StreamingService([mockProvider1.Object, mockProvider2.Object], []);
        var results = await service.SearchAllAsync("Frieren");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Title == "Frieren P1");
        Assert.Contains(results, r => r.Title == "Frieren P2");
    }

    [Fact]
    public async Task StreamingService_SearchStream_YieldsBatchesProgressively()
    {
        var mockProvider1 = new Mock<IStreamingProvider>();
        mockProvider1.SetupGet(p => p.ProviderName).Returns("FastProvider");
        mockProvider1.Setup(p => p.SearchAsync("Smartphone", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StreamingSearchResult
                {
                    Id = "101",
                    Title = "Smartphone Season 1",
                    Url = "/anime/101",
                    ProviderName = "FastProvider"
                }
            ]);

        var mockProvider2 = new Mock<IStreamingProvider>();
        mockProvider2.SetupGet(p => p.ProviderName).Returns("SlowProvider");
        mockProvider2.Setup(p => p.SearchAsync("Smartphone", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StreamingSearchResult
                {
                    Id = "102",
                    Title = "Smartphone Season 2",
                    Url = "/anime/102",
                    ProviderName = "SlowProvider"
                }
            ]);

        var service = new StreamingService([mockProvider1.Object, mockProvider2.Object], []);
        var batches = new List<IReadOnlyList<StreamingSearchResult>>();

        await foreach (var batch in service.SearchStreamAsync("Smartphone"))
        {
            batches.Add(batch);
        }

        Assert.Equal(2, batches.Count);
        Assert.Single(batches[0]);
        Assert.Single(batches[1]);
    }

    [Fact]
    public async Task StreamingService_SearchAll_FiltersByEnabledProviders()
    {
        var mockProvider1 = new Mock<IStreamingProvider>();
        mockProvider1.SetupGet(p => p.ProviderName).Returns("AniLibria");
        mockProvider1.Setup(p => p.SearchAsync("Test", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StreamingSearchResult { Id = "1", Title = "Title 1", Url = "/1", ProviderName = "AniLibria" }
            ]);

        var mockProvider2 = new Mock<IStreamingProvider>();
        mockProvider2.SetupGet(p => p.ProviderName).Returns("AnimeLib");
        mockProvider2.Setup(p => p.SearchAsync("Test", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StreamingSearchResult { Id = "2", Title = "Title 2", Url = "/2", ProviderName = "AnimeLib" }
            ]);

        var service = new StreamingService([mockProvider1.Object, mockProvider2.Object], []);

        var results = await service.SearchAllAsync("Test", enabledProviders: ["AnimeLib"]);

        Assert.Single(results);
        Assert.Equal("Title 2", results[0].Title);
        mockProvider1.Verify(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mockProvider2.Verify(p => p.SearchAsync("Test", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StreamingService_SearchStream_EmptyEnabledProviders_YieldsNothing()
    {
        var mockProvider = new Mock<IStreamingProvider>();
        mockProvider.SetupGet(p => p.ProviderName).Returns("AniLibria");

        var service = new StreamingService([mockProvider.Object], []);

        var batches = new List<IReadOnlyList<StreamingSearchResult>>();
        await foreach (var batch in service.SearchStreamAsync("Test", enabledProviders: []))
        {
            batches.Add(batch);
        }

        Assert.Empty(batches);
        mockProvider.Verify(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MpvPlayer_LoadAnimeLib_DirectUrl_LoadsFileSuccessfully()
    {
        using var player = Kiriha.Mpv.MpvPlayerBuilder.Build();
        var tcs = new TaskCompletionSource<bool>();
        string? error = null;

        player.FileLoaded += (s, e) => tcs.TrySetResult(true);
        player.PlaybackEnded += (s, e) =>
        {
            if (e.HasError)
            {
                error = e.ErrorMessage;
                tcs.TrySetResult(false);
            }
        };

        var url = "https://video1.cdnlibs.org/.%D0%B0s/uploads/converted_videos/anime/20374/players/16568/16568_1080.mp4";
        player.Load(url);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        bool loaded = completed == tcs.Task && await tcs.Task;
        Assert.True(loaded, $"Failed to load video: {error ?? "timeout"}");
    }

    [Fact]
    public async Task MpvPlayer_LoadAnimeLib_LegacyDomain_RewritesAndLoadsSuccessfully()
    {
        using var player = Kiriha.Mpv.MpvPlayerBuilder.Build();
        var tcs = new TaskCompletionSource<bool>();
        string? error = null;

        player.FileLoaded += (s, e) => tcs.TrySetResult(true);
        player.PlaybackEnded += (s, e) =>
        {
            if (e.HasError)
            {
                error = e.ErrorMessage;
                tcs.TrySetResult(false);
            }
        };

        var url = "https://video3.anilib.me/.%D0%B0s/uploads/converted_videos/anime/20374/players/16568/16568_1080.mp4";
        player.Load(url);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        bool loaded = completed == tcs.Task && await tcs.Task;
        Assert.True(loaded, $"Failed to load video: {error ?? "timeout"}");
    }

    [Fact]
    public async Task MpvPlayer_LoadAnimeLib_WithSubtitleUrl_AddsSubtitleTrackSuccessfully()
    {
        using var player = Kiriha.Mpv.MpvPlayerBuilder.Build();
        var tcs = new TaskCompletionSource<bool>();
        string? error = null;

        player.FileLoaded += (s, e) => tcs.TrySetResult(true);
        player.PlaybackEnded += (s, e) =>
        {
            if (e.HasError)
            {
                error = e.ErrorMessage;
                tcs.TrySetResult(false);
            }
        };

        var url = "https://video1.cdnlibs.org/.%D0%B0s/uploads/converted_videos/anime/20374/players/16517/16517_1080.mp4";
        player.Load(url);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        bool loaded = completed == tcs.Task && await tcs.Task;
        Assert.True(loaded, $"Failed to load video: {error ?? "timeout"}");

        var subTcs = new TaskCompletionSource<bool>();
        player.TracksChanged += () =>
        {
            var tracks = player.GetTracks();
            if (tracks.Any(t => t.Type == "sub"))
            {
                subTcs.TrySetResult(true);
            }
        };

        var tempSub = Path.Combine(Path.GetTempPath(), $"kiriha_sub_{Guid.NewGuid():N}.ass");
        await File.WriteAllTextAsync(tempSub, """
            [Script Info]
            ScriptType: v4.00+
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:05.00,Default,,0,0,0,,AnimeLib Subtitle
            """);

        try
        {
            player.AddSubtitle(tempSub);

            var subCompleted = await Task.WhenAny(subTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(subCompleted == subTcs.Task && await subTcs.Task, "Subtitle was not added to tracks");
        }
        finally
        {
            if (File.Exists(tempSub))
                File.Delete(tempSub);
        }
    }

    [Fact]
    public async Task MpvPlayer_LoadAnimeLib_LegacyMangaLibSubtitle_RewritesAndAddsSuccessfully()
    {
        // 1. Verify AnimeLibProvider rewrites legacy api2.mangalib.me subtitle URLs to api.cdnlibs.org
        var stub = new DelegatingHandlerStub(req =>
        {
            var json = """
            {
                "data": {
                    "players": [
                        {
                            "player": "animelib",
                            "team": { "name": "AniLibria" },
                            "video": {
                                "quality": [
                                    { "quality": 1080, "href": "1080.mp4" }
                                ]
                            },
                            "video_domain": "video3.anilib.me",
                            "subtitles": [
                                {
                                    "format": "ass",
                                    "src": "https://api2.mangalib.me/uploads/subtitles/355977/5c6b806b-b197-457b-b84f-c4f145bf9dee.ass"
                                }
                            ]
                        }
                    ]
                }
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(stub);
        var provider = new AnimeLibProvider(client);
        var sources = await provider.GetEpisodeSourcesAsync("19309", 1);

        Assert.NotEmpty(sources);
        var source = sources[0];
        Assert.Equal("https://api.cdnlibs.org/uploads/subtitles/355977/5c6b806b-b197-457b-b84f-c4f145bf9dee.ass", source.SubtitleUrl);

        // 2. Verify MpvPlayer adds subtitle track
        using var player = Kiriha.Mpv.MpvPlayerBuilder.Build();
        var tcs = new TaskCompletionSource<bool>();
        string? error = null;

        player.FileLoaded += (s, e) => tcs.TrySetResult(true);
        player.PlaybackEnded += (s, e) =>
        {
            if (e.HasError)
            {
                error = e.ErrorMessage;
                tcs.TrySetResult(false);
            }
        };

        var url = "https://video1.cdnlibs.org/.%D0%B0s/uploads/converted_videos/anime/19309/players/27819/27819_1080.mp4";
        player.Load(url);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        bool loaded = completed == tcs.Task && await tcs.Task;
        Assert.True(loaded, $"Failed to load video: {error ?? "timeout"}");

        var subTcs = new TaskCompletionSource<bool>();
        player.TracksChanged += () =>
        {
            var tracks = player.GetTracks();
            if (tracks.Any(t => t.Type == "sub"))
            {
                subTcs.TrySetResult(true);
            }
        };

        var tempSub = Path.Combine(Path.GetTempPath(), $"kiriha_sub_legacy_{Guid.NewGuid():N}.ass");
        await File.WriteAllTextAsync(tempSub, """
            [Script Info]
            ScriptType: v4.00+
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:05.00,Default,,0,0,0,,Legacy Subtitle
            """);

        try
        {
            player.AddSubtitle(tempSub);

            var subCompleted = await Task.WhenAny(subTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(subCompleted == subTcs.Task && await subTcs.Task, "Subtitle was not added to tracks");
        }
        finally
        {
            if (File.Exists(tempSub))
                File.Delete(tempSub);
        }
    }

    [Fact]
    public async Task DreamCast_SearchAndGetAnimeDetails_ReturnsEpisodesAndHlsSources()
    {
        var rawPlaylist = "https://stream.dreamerscast.com/120/1/master.m3u8||||https://stream.dreamerscast.com/120/2/master.m3u8";
        var b64Playlist = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawPlaylist));

        var stub = new DelegatingHandlerStub(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            if (req.Method == HttpMethod.Post)
            {
                var releasesJson = """
                {
                    "releases": [
                        {
                            "id": 120,
                            "russian": "Reincarnated as a Sword",
                            "original": "Tensei shitara Ken Deshita",
                            "image": "//dreamerscast.com/posters/120.jpg",
                            "url": "/home/release/120"
                        }
                    ]
                }
                """;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(releasesJson, Encoding.UTF8, "application/json")
                };
            }

            if (url.Contains("/home/release/120"))
            {
                var pageHtml = $"""
                <!DOCTYPE html>
                <html>
                <body>
                    <div id="player"></div>
                    <script>
                        var playlist = atob("{b64Playlist}");
                    </script>
                </body>
                </html>
                """;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(pageHtml, Encoding.UTF8, "text/html")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(stub);
        var provider = new DreamCastProvider(client);

        var searchResults = await provider.SearchAsync("Reincarnated as a Sword");
        Assert.NotEmpty(searchResults);

        var target = searchResults.FirstOrDefault(r => r.Id.Contains("120")) ?? searchResults.First();
        Assert.StartsWith("/home/release/", target.Id);
        Assert.StartsWith("/home/release/", target.Url);

        var details = await provider.GetAnimeDetailsAsync(target.Id);
        Assert.NotNull(details);
        Assert.NotEmpty(details.Episodes);
        Assert.Contains(details.Episodes, e => e.Sources.Any(s => s.PlayerUrl.Contains(".m3u8")));
    }

    [Fact]
    public void Anime365HostResolver_DefaultMirror_IsCo()
    {
        var resolver = new Anime365HostResolver();
        Assert.Equal(Kiriha.Core.Streaming.Constants.StreamingConstants.Anime365.MirrorCo, resolver.ActiveMirror);
    }

    [Theory]
    [InlineData("https://smotret-anime.org/api/series", "https://smotret-anime.co/api/series")]
    [InlineData("https://smotret-anime.app/translations/embed/123", "https://smotret-anime.co/translations/embed/123")]
    [InlineData("https://smotretanime.co/api/me", "https://smotret-anime.co/api/me")]
    [InlineData("https://smotret-anime.com/test", "https://smotret-anime.co/test")]
    [InlineData("https://google.com/search", "https://google.com/search")]
    public void Anime365HostResolver_Rewrite_RewritesAppropriately(string input, string expected)
    {
        var resolver = new Anime365HostResolver();
        var result = resolver.Rewrite(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Anime365Extractor_CanExtract_KnownMirrors_ReturnsTrue()
    {
        using var client = new HttpClient();
        var extractor = new Anime365Extractor(client);

        Assert.True(extractor.CanExtract("https://smotret-anime.co/translations/embed/12345"));
        Assert.True(extractor.CanExtract("https://smotretanime.co/translations/embed/12345"));
        Assert.True(extractor.CanExtract("https://smotret-anime.com/translations/embed/12345"));
        Assert.True(extractor.CanExtract("https://smotret-anime.org/translations/embed/12345"));
        Assert.True(extractor.CanExtract("https://smotret-anime.app/translations/embed/12345"));
        Assert.True(extractor.CanExtract("https://anime365.org/translations/embed/12345"));
        Assert.False(extractor.CanExtract("https://google.com/embed/12345"));
    }

    [Fact]
    public void Anime365Extractor_ParseStreamsFromData_ArrayStreams_ExtractsQualityDescending()
    {
        var json = """
        {
            "stream": [
                { "height": 720, "urls": [ "https://cdn.example.com/720p.mp4", "https://backup/720p.mp4" ] },
                { "height": 1080, "urls": [ "https://cdn.example.com/1080p.mp4" ] }
            ],
            "download": [
                { "height": 1080, "url": "https://smotret-anime.co/download/1080.mp4" }
            ],
            "subtitlesUrl": "https://smotret-anime.co/episodeTranslations/123.ass"
        }
        """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var streams = Anime365Extractor.ParseStreamsFromData(doc.RootElement, "https://smotret-anime.co/episodeTranslations/123.ass");

        Assert.Equal(2, streams.Count);
        Assert.Equal(1080, streams[0].Quality);
        Assert.Equal("https://cdn.example.com/1080p.mp4", streams[0].Url);
        Assert.Equal("https://smotret-anime.co/episodeTranslations/123.ass", streams[0].SubtitleUrl);

        Assert.Equal(720, streams[1].Quality);
        Assert.Equal("https://cdn.example.com/720p.mp4", streams[1].Url);
    }

    [Fact]
    public void Anime365Extractor_ParseStreamsFromData_DownloadFallback_ExtractsWhenStreamEmpty()
    {
        var json = """
        {
            "download": [
                { "height": 1080, "url": "https://smotret-anime.co/download/1080.mp4" }
            ]
        }
        """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var streams = Anime365Extractor.ParseStreamsFromData(doc.RootElement, null);

        Assert.Single(streams);
        Assert.Equal(1080, streams[0].Quality);
        Assert.Equal("https://smotret-anime.co/download/1080.mp4", streams[0].Url);
    }

    [Fact]
    public async Task Anime365Extractor_ExtractVideosAsync_WithTokenAndArrayStream_ResolvesStreamsAndSubtitles()
    {
        var stub = new DelegatingHandlerStub(req =>
        {
            var json = """
            {
                "data": {
                    "id": 6017853,
                    "stream": [
                        { "height": 1080, "urls": [ "https://bcdn.melon-soda.org/route/1080.mp4" ] }
                    ],
                    "subtitlesUrl": "/episodeTranslations/6017853.ass?willcache"
                }
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(stub);
        var resolver = new Anime365HostResolver();
        var extractor = new Anime365Extractor(client, hostResolver: resolver);

        var videos = await extractor.ExtractVideosAsync("https://smotret-anime.co/translations/embed/6017853?access_token=test_tok");

        Assert.Single(videos);
        Assert.Equal("https://bcdn.melon-soda.org/route/1080.mp4", videos[0].Url);
        Assert.Equal(1080, videos[0].Quality);
        Assert.Equal("mp4", videos[0].VideoType);
        Assert.Equal("https://smotret-anime.co/episodeTranslations/6017853.ass?willcache", videos[0].SubtitleUrl);
    }

    [Fact]
    public async Task Anime365Provider_GetAnimeDetailsAsync_ParsesEpisodesAndDescriptionsArraySuccessfully()
    {
        var stub = new DelegatingHandlerStub(req =>
        {
            var json = """
            {
                "data": {
                    "id": 32689,
                    "titles": { "ru": "Test Sword S2", "romaji": "Tensei shitara Ken deshita II" },
                    "posterUrl": "https://smotret-anime.app/posters/32689.jpg",
                    "descriptions": [
                        { "source": "shikimori.me", "value": "A heroic sword adventure." }
                    ],
                    "episodes": [
                        { "id": 385859, "episodeFull": "Episode 1", "episodeInt": 1, "isActive": 1 },
                        { "id": 387071, "episodeFull": "Episode 2", "episodeInt": 2, "isActive": 1 }
                    ]
                }
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(stub);
        var provider = new Anime365Provider(client);

        var details = await provider.GetAnimeDetailsAsync("32689");

        Assert.NotNull(details);
        Assert.Equal("32689", details.Id);
        Assert.Equal("Test Sword S2", details.Title);
        Assert.Equal("A heroic sword adventure.", details.Description);
        Assert.Equal(2, details.Episodes.Count);
        Assert.Equal(1, details.Episodes[0].EpisodeNumber);
        Assert.Equal("385859", details.Episodes[0].EpisodeId);
        Assert.Equal("Episode 1", details.Episodes[0].Title);
        Assert.Equal(2, details.Episodes[1].EpisodeNumber);
        Assert.Equal("387071", details.Episodes[1].EpisodeId);
    }

    [Fact]
    public async Task Anime365Provider_GetEpisodeSourcesAsync_WithEpisodeId_ReturnsSourcesWithLanguage()
    {
        var stub = new DelegatingHandlerStub(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("/api/translations"))
            {
                var transJson = """
                {
                    "data": [
                        {
                            "id": 6040198,
                            "authorsSummary": "JAM CLUB",
                            "typeKind": "voice",
                            "typeLang": "ru",
                            "height": 1080,
                            "isActive": 1
                        },
                        {
                            "id": 6036012,
                            "authorsSummary": "Crunchyroll",
                            "typeKind": "sub",
                            "typeLang": "en",
                            "height": 1080,
                            "isActive": 1
                        },
                        {
                            "id": 6036013,
                            "authorsSummary": "Crunchyroll",
                            "typeKind": "sub",
                            "typeLang": "ru",
                            "height": 1080,
                            "isActive": 1
                        }
                    ]
                }
                """;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(transJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(stub);
        var provider = new Anime365Provider(client);

        var sources = await provider.GetEpisodeSourcesAsync("385859", 1);

        Assert.Equal(3, sources.Count);
        Assert.Equal("JAM CLUB [RU] [1080p]", sources[0].Title);
        Assert.Equal("JAM CLUB", sources[0].Dubber);
        Assert.Equal("1080p", sources[0].Quality);
        Assert.Equal("ru", sources[0].Language);
        Assert.Contains("6040198", sources[0].PlayerUrl);

        Assert.Equal("Crunchyroll (Sub) [EN] [1080p]", sources[1].Title);
        Assert.Equal("Crunchyroll", sources[1].Dubber);
        Assert.Equal("en", sources[1].Language);

        Assert.Equal("Crunchyroll (Sub) [RU] [1080p]", sources[2].Title);
        Assert.Equal("Crunchyroll", sources[2].Dubber);
        Assert.Equal("ru", sources[2].Language);
    }

    [Fact]
    public void OnlineSourceItem_LanguageProperties_FormattedAndDetectedProperly()
    {
        var sourceWithLang = new StreamingSource
        {
            Title = "Crunchyroll (Sub) [EN] [1080p]",
            PlayerUrl = "https://example.com/embed/1",
            Dubber = "Crunchyroll",
            Language = "en"
        };
        var item1 = new OnlineSourceItem(sourceWithLang);
        Assert.True(item1.HasLanguage);
        Assert.Equal("EN", item1.LanguageDisplay);
        Assert.Equal("Crunchyroll", item1.CleanStudioName);
        Assert.True(item1.IsSubtitles);

        Assert.False(item1.IsRussian);
        Assert.True(item1.IsNonRussian);

        var sourceWithoutLang = new StreamingSource
        {
            Title = "SubsPlease [JA] (1080p)",
            PlayerUrl = "https://example.com/embed/2",
            Dubber = "SubsPlease"
        };
        var item2 = new OnlineSourceItem(sourceWithoutLang);
        Assert.True(item2.HasLanguage);
        Assert.Equal("JA", item2.LanguageDisplay);
        Assert.Equal("SubsPlease", item2.CleanStudioName);
        Assert.False(item2.IsRussian);
        Assert.True(item2.IsNonRussian);

        var russianSource = new StreamingSource
        {
            Title = "Rom_Rem [1080p]",
            PlayerUrl = "https://example.com/embed/3",
            Dubber = "Rom_Rem",
            Language = "ru"
        };
        var item3 = new OnlineSourceItem(russianSource);
        Assert.True(item3.IsRussian);
        Assert.False(item3.IsNonRussian);

        var untaggedRussian = new StreamingSource
        {
            Title = "AniLibria (1080p)",
            PlayerUrl = "https://example.com/embed/4",
            Dubber = "AniLibria"
        };
        var item4 = new OnlineSourceItem(untaggedRussian);
        Assert.True(item4.IsRussian);
        Assert.False(item4.IsNonRussian);
    }

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
