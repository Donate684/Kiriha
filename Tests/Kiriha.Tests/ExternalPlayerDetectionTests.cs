using System;
using System.IO;
using System.Linq;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Platform;
using Kiriha.Infrastructure.Tracking.Anisthesia;
using Kiriha.Infrastructure.Tracking.Anisthesia.Strategies;
using Kiriha.Mpv.UI.ViewModels.Player;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class ExternalPlayerDetectionTests
{
    private readonly AnisthesiaPlayer _mpcHcPlayer;
    private readonly AnisthesiaPlayer _mpvPlayer;
    private readonly AnisthesiaPlayer _vlcPlayer;

    public ExternalPlayerDetectionTests()
    {
        using var stream = typeof(AnisthesiaPlayerLoader).Assembly
            .GetManifestResourceStream("Kiriha.Infrastructure.Tracking.Anisthesia.players.anisthesia");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var data = reader.ReadToEnd();
        var players = PlayerParser.ParseData(data);

        _mpcHcPlayer = players.First(p => p.Name.Equals("MPC-HC", StringComparison.OrdinalIgnoreCase));
        _mpvPlayer = players.First(p => p.Name.Equals("mpv", StringComparison.OrdinalIgnoreCase));
        _vlcPlayer = players.First(p => p.Name.Equals("VLC media player", StringComparison.OrdinalIgnoreCase));
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
    public void PlayerSelectionViewModel_DefaultAllowedProcessesEmpty_EnablesVideoPlayers()
    {
        var settings = new AppSettings();
        settings.System.Scrobbler.AllowedProcesses.Clear(); // empty by default

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        var mockDetector = new Mock<IExternalMediaDetector>();
        mockDetector.Setup(d => d.AvailablePlayers).Returns(new System.Collections.Generic.List<AnisthesiaPlayer>
        {
            new AnisthesiaPlayer { Name = "mpv", Type = PlayerType.Default },
            new AnisthesiaPlayer { Name = "Google Chrome", Type = PlayerType.WebBrowser }
        });
        mockDetector.Setup(d => d.RunningPlayerNames).Returns(new System.Collections.Generic.HashSet<string>());

        using var vm = new PlayerSelectionViewModel(mockDetector.Object, mockSettings.Object);

        var mpvItem = vm.VideoPlayers.FirstOrDefault(p => p.Name == "mpv");
        Assert.NotNull(mpvItem);
        Assert.True(mpvItem!.IsEnabled);

        var chromeItem = vm.WebBrowsers.FirstOrDefault(p => p.Name == "Google Chrome");
        Assert.NotNull(chromeItem);
        Assert.False(chromeItem!.IsEnabled);
    }

    private static ParsedMedia? ParseSimulatedTitle(AnisthesiaPlayer player, string windowTitle)
    {
        if (string.IsNullOrEmpty(player.WindowTitleFormat)) return null;

        var match = System.Text.RegularExpressions.Regex.Match(windowTitle, player.WindowTitleFormat);
        if (!match.Success) return null;

        for (int i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success && !string.IsNullOrEmpty(match.Groups[i].Value))
            {
                string extracted = match.Groups[i].Value;
                var elements = Kiriha.Utils.Parsing.AnimeParseCache.Parse(extracted);
                var titleElement = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
                var episode = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;

                return new ParsedMedia
                {
                    OriginalTitle = extracted,
                    AnimeTitle = titleElement != null ? titleElement.Value : extracted,
                    Episode = episode ?? string.Empty,
                    IsPlaying = true
                };
            }
        }

        return null;
    }
}
