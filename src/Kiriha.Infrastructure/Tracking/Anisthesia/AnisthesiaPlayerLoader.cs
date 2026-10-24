using Kiriha.Core.Domain.Models;

namespace Kiriha.Infrastructure.Tracking.Anisthesia;

/// <summary>
/// Provides a static catalog of known media players and their detection rules.
/// </summary>
public static class AnisthesiaPlayerLoader
{
    private static readonly AnisthesiaPlayer[] Players =
    [
        new()
        {
            Name = "MPC-HC",
            WindowClasses = ["MediaPlayerClassicW"],
            Executables = ["mpc-hc", "mpc-hc64", "^mpc-hc.+", "iris", "shoukaku"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^Media Player Classic Home Cinema$|^MPC-HC$|^(.+) - MPC-HC.*|(.+)",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "MPC-BE",
            WindowClasses = ["MediaPlayerClassicW", "MPC-BE"],
            Executables = ["mpc-be", "mpc-be64"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^MPC-BE.*|(.+) - MPC-BE.*",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "mpv",
            WindowClasses = ["mpv"],
            Executables = ["mpv"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^No file - mpv|(.+) - mpv|mpv - (.+)",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "mpv.net",
            WindowClasses = [@"^WindowsForms10\.Window\.8\.app\..+"],
            Executables = ["mpvnet"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^mpv\.net.*|(.+) - mpv\.net.*",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "VLC media player",
            WindowClasses = ["^Qt.+QWindowIcon", "QWidget", "SkinWindowClass"],
            Executables = ["vlc"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^VLC media player|(.+) - VLC media player|.+",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "PotPlayer",
            WindowClasses = ["PotPlayer", "PotPlayer64"],
            Executables = ["PotPlayer", "PotPlayer64", "PotPlayerMini", "PotPlayerMini64", "sumire", "zuikaku"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^PotPlayer|(.+) - PotPlayer",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "KMPlayer",
            WindowClasses = ["KMPlayer 64X", "TApplication"],
            Executables = ["KMPlayer", "KMPlayer64"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^(?:The )?KMPlayer|(?:\[\d+/\d+\] )?(.+) - (?:The )?KMPlayer|(.+)",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "SMPlayer",
            WindowClasses = ["^Qt.+QWindowIcon", "QWidget"],
            Executables = ["smplayer", "smplayer2"],
            Strategies = [StrategyType.WindowTitle],
            WindowTitleFormat = @"^SMPlayer|(.+) - SMPlayer|.+",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "GOM Player",
            WindowClasses = ["GomPlayer1.x", "GomPlayerPlus32_2.x", "GomPlayerPlus64_2.x"],
            Executables = ["GOM", "GOM64"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^GOM Player(?: Plus)|(.+)(?:\[Subtitle\]) - GOM Player(?: Plus)",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Media Player Classic",
            WindowClasses = ["MediaPlayerClassicW"],
            Executables = ["mplayerc", "mplayerc64"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^Media Player Classic|(.+) - Media Player Classic",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Media Player Classic Qute Theater",
            WindowClasses = ["^Qt.+QWindowIcon"],
            Executables = ["mpc-qt"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^Media Player Classic Qute Theater|Media Player Classic Qute Theater - (.+)|.+",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Baka MPlayer",
            WindowClasses = ["Qt5QWindowIcon"],
            Executables = ["Baka MPlayer"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^Baka MPlayer|(.+)",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Memento",
            WindowClasses = ["^Qt.+QWindow(OwnDC)?Icon"],
            Executables = ["memento"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^Memento|(.+) - Memento",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Kodi",
            WindowClasses = ["Kodi", "XBMC"],
            Executables = ["kodi", "XBMC"],
            Strategies = [StrategyType.OpenFiles],
            WindowTitleFormat = string.Empty,
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Zoom Player",
            WindowClasses = ["TApplication"],
            Executables = ["zplayer"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^Zoom Player|(.+) - Zoom Player (?:FREE|MAX)",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Windows Media Player",
            WindowClasses = ["WMPlayerApp", "WMP Skin Host"],
            Executables = ["wmplayer"],
            Strategies = [StrategyType.OpenFiles],
            WindowTitleFormat = string.Empty,
            Type = PlayerType.Default
        },
        new()
        {
            Name = "WebTorrent Desktop",
            WindowClasses = ["Chrome_WidgetWin_1"],
            Executables = ["WebTorrent"],
            Strategies = [StrategyType.WindowTitle],
            WindowTitleFormat = @"^WebTorrent(?: \(BETA\))?|Main Window|Preferences|About WebTorrent.*|(.+)",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "5KPlayer",
            WindowClasses = ["Qt5QWindowIcon"],
            Executables = ["5KPlayer"],
            Strategies = [StrategyType.OpenFiles],
            WindowTitleFormat = string.Empty,
            Type = PlayerType.Default
        },
        new()
        {
            Name = "ALLPlayer",
            WindowClasses = ["TApplication"],
            Executables = ["ALLPlayer"],
            Strategies = [StrategyType.OpenFiles],
            WindowTitleFormat = string.Empty,
            Type = PlayerType.Default
        },
        new()
        {
            Name = "BS.Player",
            WindowClasses = ["BSPlayer"],
            Executables = ["bsplayer"],
            Strategies = [StrategyType.OpenFiles],
            WindowTitleFormat = string.Empty,
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Light Alloy",
            WindowClasses = ["TApplication"],
            Executables = ["LA"],
            Strategies = [StrategyType.OpenFiles, StrategyType.WindowTitle],
            WindowTitleFormat = @"^Light Alloy.*|(.+) - Light Alloy.*",
            Type = PlayerType.Default
        },
        new()
        {
            Name = "Splash",
            WindowClasses = ["DX_DISPLAY0"],
            Executables = ["Splash", "SplashLite"],
            Strategies = [StrategyType.OpenFiles],
            WindowTitleFormat = string.Empty,
            Type = PlayerType.Default
        }
    ];

    public static IReadOnlyList<AnisthesiaPlayer> Load() => Players;
}
