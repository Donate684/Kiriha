using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Platform;
using Kiriha.Infrastructure.Tracking.ExternalDetection;
using Kiriha.Infrastructure.Tracking.ExternalDetection.Strategies;
using Kiriha.Mpv.UI.ViewModels.Player;
using Moq;

namespace Kiriha.Tests;

public class ExternalPlayerDetectionTests
{
    private readonly ExternalPlayerDefinition _mpcHcPlayer;
    private readonly ExternalPlayerDefinition _mpvPlayer;
    private readonly ExternalPlayerDefinition _vlcPlayer;

    public ExternalPlayerDetectionTests()
    {
        var players = ExternalPlayerCatalog.Load();

        _mpcHcPlayer = players.First(p => p.Name.Equals("MPC-HC", StringComparison.OrdinalIgnoreCase));
        _mpvPlayer = players.First(p => p.Name.Equals("mpv", StringComparison.OrdinalIgnoreCase));
        _vlcPlayer = players.First(p => p.Name.Equals("VLC media player", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExternalPlayerCatalog_Load_ReturnsPopulatedPlayerCatalog()
    {
        var players = ExternalPlayerCatalog.Load();
        Assert.NotNull(players);
        Assert.NotEmpty(players);

        string[] requiredPlayers = ["MPC-HC", "MPC-BE", "mpv", "VLC media player", "PotPlayer"];
        foreach (var requiredName in requiredPlayers)
        {
            var player = players.FirstOrDefault(p => p.Name.Equals(requiredName, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(player);
            Assert.NotEmpty(player!.Executables);
            Assert.NotEmpty(player.Strategies);
        }
    }

    [Fact]
    public void MpcHc_WindowRegex_ExtractsPlayingTitle_WithSuffix()
    {
        const string title = "[SubsPlease] Sousou no Frieren - 01 (1080p).mkv - MPC-HC";
        var parsed = ParseSimulatedTitle(_mpcHcPlayer, title);

        Assert.NotNull(parsed);
        Assert.Equal("Sousou no Frieren", parsed!.AnimeTitle);
        Assert.Equal("01", parsed.Episode);
    }

    [Fact]
    public void MpcHc_WindowRegex_ExtractsPlayingTitle_WithoutSuffix()
    {
        const string title = "[SubsPlease] Sousou no Frieren - 01 (1080p).mkv";
        var parsed = ParseSimulatedTitle(_mpcHcPlayer, title);

        Assert.NotNull(parsed);
        Assert.Equal("Sousou no Frieren", parsed!.AnimeTitle);
        Assert.Equal("01", parsed.Episode);
    }

    [Theory]
    [InlineData("MPC-HC")]
    [InlineData("Media Player Classic Home Cinema")]
    public void MpcHc_WindowRegex_ReturnsNull_WhenIdle(string idleTitle)
    {
        var parsed = ParseSimulatedTitle(_mpcHcPlayer, idleTitle);
        Assert.Null(parsed);
    }

    [Fact]
    public void Mpv_WindowRegex_ExtractsPlayingTitle()
    {
        const string title = "[SubsPlease] Sousou no Frieren - 05 (1080p) - mpv";
        var parsed = ParseSimulatedTitle(_mpvPlayer, title);

        Assert.NotNull(parsed);
        Assert.Equal("Sousou no Frieren", parsed!.AnimeTitle);
        Assert.Equal("05", parsed.Episode);
    }

    [Fact]
    public void Mpv_WindowRegex_ReturnsNull_WhenIdle()
    {
        var parsed = ParseSimulatedTitle(_mpvPlayer, "No file - mpv");
        Assert.Null(parsed);
    }

    [Fact]
    public void Vlc_WindowRegex_ExtractsPlayingTitle()
    {
        const string title = "[SubsPlease] Sousou no Frieren - 12 (1080p).mkv - VLC media player";
        var parsed = ParseSimulatedTitle(_vlcPlayer, title);

        Assert.NotNull(parsed);
        Assert.Equal("Sousou no Frieren", parsed!.AnimeTitle);
        Assert.Equal("12", parsed.Episode);
    }

    [Fact]
    public void Vlc_WindowRegex_ReturnsNull_WhenIdle()
    {
        var parsed = ParseSimulatedTitle(_vlcPlayer, "VLC media player");
        Assert.Null(parsed);
    }

    [Fact]
    public void KmPlayer_WindowRegex_ExtractsPlayingTitle_WithPlaylistPrefix()
    {
        var kmPlayer = ExternalPlayerCatalog.Load().First(p => p.Name.Equals("KMPlayer", StringComparison.OrdinalIgnoreCase));
        var parsed = ParseSimulatedTitle(kmPlayer, "[1/12] [SubsPlease] Sousou no Frieren - 03 (1080p).mkv - The KMPlayer");

        Assert.NotNull(parsed);
        Assert.Equal("Sousou no Frieren", parsed!.AnimeTitle);
        Assert.Equal("03", parsed.Episode);
    }

    [Fact]
    public void WebTorrent_WindowRegex_ReturnsNull_WhenBetaIdle()
    {
        var webTorrent = ExternalPlayerCatalog.Load().First(p => p.Name.Equals("WebTorrent Desktop", StringComparison.OrdinalIgnoreCase));
        var parsed = ParseSimulatedTitle(webTorrent, "WebTorrent (BETA)");
        Assert.Null(parsed);
    }

    [Fact]
    public void MediaTitleResolver_FromExtractedTitle_ResolvesReleaseFields()
    {
        var media = MediaTitleResolver.FromExtractedTitle("[SubsPlease] Dandadan - 04 (1080p) [ABCD1234].mkv");
        Assert.Equal("Dandadan", media.AnimeTitle);
        Assert.Equal("04", media.Episode);
        Assert.Equal("SubsPlease", media.Group);
        Assert.True(media.IsPlaying);
    }

    [Fact]
    public void Win32Api_GetWindowTitle_ReturnsEmpty_ForZeroHandle()
    {
        var title = Win32Api.GetWindowTitle(IntPtr.Zero);
        Assert.Equal(string.Empty, title);
    }

    [Fact]
    public void Win32Api_FindMainWindow_ReturnsZero_ForZeroPid()
    {
        var hwnd = Win32Api.FindMainWindow(0);
        Assert.Equal(IntPtr.Zero, hwnd);
    }

    [Fact]
    public void HandleEnumerationStrategy_GetOpenFiles_ReturnsEmpty_ForZeroPid()
    {
        var files = HandleEnumerationStrategy.GetOpenFiles(0);
        Assert.NotNull(files);
        Assert.Empty(files);
    }

    [Fact]
    public void HandleEnumerationStrategy_Apply_ReturnsNull_ForZeroPid()
    {
        var result = HandleEnumerationStrategy.Apply(_mpcHcPlayer, 0);
        Assert.Null(result);
    }

    [Fact]
    public void HandleEnumerationStrategy_TryGetFirstOpenVideoFile_ReturnsNull_ForZeroPid()
    {
        var result = HandleEnumerationStrategy.TryGetFirstOpenVideoFile(0);
        Assert.Null(result);
    }

    [Fact]
    public void PlayerSelectionViewModel_DefaultAllowedProcessesEmpty_EnablesVideoPlayers()
    {
        var settings = new AppSettings();
        settings.System.Scrobbler.AllowedProcesses.Clear(); // empty by default

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var mockDetector = new Mock<IExternalMediaDetector>();
        mockDetector.Setup(d => d.AvailablePlayers).Returns(new System.Collections.Generic.List<ExternalPlayerDefinition>
        {
            new ExternalPlayerDefinition { Name = "mpv", Type = PlayerType.Default },
            new ExternalPlayerDefinition { Name = "Google Chrome", Type = PlayerType.WebBrowser }
        });
        mockDetector.Setup(d => d.RunningPlayerNames).Returns(new System.Collections.Generic.HashSet<string>());

        using var vm = new PlayerSelectionViewModel(mockDetector.Object, mockSettings.Object);

        var mpvItem = vm.VideoPlayers.FirstOrDefault(p => p.Name == "mpv");
        Assert.NotNull(mpvItem);
        Assert.True(mpvItem!.IsEnabled);

        var chromeItem = vm.VideoPlayers.FirstOrDefault(p => p.Name == "Google Chrome");
        Assert.Null(chromeItem);
    }

    private static ParsedMedia? ParseSimulatedTitle(ExternalPlayerDefinition player, string windowTitle)
    {
        if (string.IsNullOrEmpty(player.WindowTitleFormat)) return null;

        var match = System.Text.RegularExpressions.Regex.Match(windowTitle, player.WindowTitleFormat);
        if (!match.Success) return null;

        for (int i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success && !string.IsNullOrEmpty(match.Groups[i].Value))
            {
                string extracted = match.Groups[i].Value;
                var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(extracted);

                return new ParsedMedia
                {
                    OriginalTitle = extracted,
                    AnimeTitle = !string.IsNullOrEmpty(parsed.Title) ? parsed.Title : extracted,
                    Episode = parsed.Episode,
                    IsPlaying = true
                };
            }
        }

        return null;
    }
}
