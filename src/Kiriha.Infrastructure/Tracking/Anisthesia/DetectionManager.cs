using System.Collections.Frozen;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Platform;
using Kiriha.Infrastructure.Tracking.Anisthesia.Strategies;
using Serilog;

namespace Kiriha.Infrastructure.Tracking.Anisthesia;

public class DetectionManager
{
    private readonly ISettingsService _settingsService;
    private readonly FrozenDictionary<string, List<AnisthesiaPlayer>> _exactExecutableMap;
    private readonly (Regex Regex, AnisthesiaPlayer Player, List<AnisthesiaPlayer> PlayerList)[] _regexExecutableRules;
    private readonly FrozenDictionary<AnisthesiaPlayer, Regex> _windowTitleRegexMap;

    private static readonly FrozenSet<string> JunkPatterns = FrozenSet.ToFrozenSet(
    [
        "vlc media player", "mpc-hc", "potplayer", "mpv", "kmplayer", "zoom player", "ready", "opening..."
    ], StringComparer.OrdinalIgnoreCase);

    public DetectionManager(IReadOnlyList<AnisthesiaPlayer> players, ISettingsService settingsService)
    {
        _settingsService = settingsService;

        var exactDict = new Dictionary<string, List<AnisthesiaPlayer>>(StringComparer.OrdinalIgnoreCase);
        var regexList = new List<(Regex, AnisthesiaPlayer, List<AnisthesiaPlayer>)>();
        var windowTitleDict = new Dictionary<AnisthesiaPlayer, Regex>();

        foreach (var player in players)
        {
            foreach (var exe in player.Executables)
            {
                if (exe.StartsWith('^'))
                {
                    regexList.Add((new Regex(exe, RegexOptions.IgnoreCase | RegexOptions.Compiled), player, new List<AnisthesiaPlayer>(1) { player }));
                }
                else
                {
                    if (!exactDict.TryGetValue(exe, out var list))
                    {
                        list = new List<AnisthesiaPlayer>(1);
                        exactDict[exe] = list;
                    }
                    list.Add(player);
                }
            }

            if (!string.IsNullOrEmpty(player.WindowTitleFormat))
            {
                try
                {
                    windowTitleDict[player] = new Regex(player.WindowTitleFormat, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to compile WindowTitleFormat regex for player {Player}", player.Name);
                }
            }
        }

        _exactExecutableMap = exactDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _regexExecutableRules = regexList.ToArray();
        _windowTitleRegexMap = windowTitleDict.ToFrozenDictionary();
    }

    private readonly List<(uint Pid, string ProcessName)> _processBuffer = new(350);

    public Task<(HashSet<string> RunningPlayers, ParsedMedia? Media)> DetectSessionAsync()
    {
        return Task.FromResult(DetectSession());
    }

    private List<AnisthesiaPlayer>? GetMatchingPlayers(string procName)
    {
        if (_exactExecutableMap.TryGetValue(procName, out var exactMatches))
        {
            return exactMatches;
        }

        if (_regexExecutableRules.Length > 0)
        {
            List<AnisthesiaPlayer>? matchingPlayers = null;
            for (int r = 0; r < _regexExecutableRules.Length; r++)
            {
                if (_regexExecutableRules[r].Regex.IsMatch(procName))
                {
                    if (matchingPlayers is null)
                    {
                        matchingPlayers = _regexExecutableRules[r].PlayerList;
                    }
                    else
                    {
                        if (ReferenceEquals(matchingPlayers, _regexExecutableRules[r].PlayerList))
                        {
                            matchingPlayers = new List<AnisthesiaPlayer>(matchingPlayers);
                        }
                        matchingPlayers.Add(_regexExecutableRules[r].Player);
                    }
                }
            }
            return matchingPlayers;
        }

        return null;
    }

    public (HashSet<string> RunningPlayers, ParsedMedia? Media) DetectSession()
    {
        HashSet<string> running = [with(StringComparer.OrdinalIgnoreCase)];
        if (!OperatingSystem.IsWindows()) return (running, null);

        var allowedProcesses = _settingsService.Current?.System?.Scrobbler?.AllowedProcesses;
        var hasFilter = allowedProcesses != null && allowedProcesses.Count > 0;
        ParsedMedia? detectedMedia = null;

        if (WindowsProcessSnapshot.TryEnumerateProcesses(_processBuffer))
        {
            for (int p = 0; p < _processBuffer.Count; p++)
            {
                var (pid, procName) = _processBuffer[p];
                if (procName.Equals("Kiriha", StringComparison.OrdinalIgnoreCase) || pid == Environment.ProcessId)
                    continue;

                try
                {
                    var matchingPlayers = GetMatchingPlayers(procName);
                    if (matchingPlayers is null || matchingPlayers.Count == 0) continue;

                    for (int i = 0; i < matchingPlayers.Count; i++)
                    {
                        running.Add(matchingPlayers[i].Name);
                    }

                    if (detectedMedia != null) continue;

                    var result = TryDetectFromProcess(pid, procName, matchingPlayers, hasFilter, allowedProcesses);
                    if (result != null)
                    {
                        detectedMedia = result;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                {
                    Log.Debug(ex, "Process access denied or process exited while inspecting {ProcessName}", procName);
                }
            }
        }

        return (running, detectedMedia);
    }

    private ParsedMedia? TryDetectFromProcess(
        uint pid,
        string procName,
        IReadOnlyList<AnisthesiaPlayer> matchingPlayers,
        bool hasFilter,
        List<string>? allowedProcesses)
    {
        IntPtr hWnd = IntPtr.Zero;
        bool hWndEvaluated = false;

        for (int i = 0; i < matchingPlayers.Count; i++)
        {
            var player = matchingPlayers[i];

            if (!hasFilter)
            {
                if (player.Type == PlayerType.WebBrowser) continue;
            }
            else if (allowedProcesses?.Contains(player.Name) != true)
            {
                continue;
            }

            foreach (var strategy in player.Strategies)
            {
                ParsedMedia? result = null;
                if (strategy == StrategyType.OpenFiles)
                {
                    result = HandleEnumerationStrategy.Apply(player, pid);
                }
                else if (strategy == StrategyType.WindowTitle)
                {
                    if (!hWndEvaluated)
                    {
                        hWnd = Win32Api.FindMainWindow(pid);
                        hWndEvaluated = true;
                    }

                    if (hWnd != IntPtr.Zero)
                    {
                        _windowTitleRegexMap.TryGetValue(player, out var compiledFormat);
                        result = WindowTitleStrategy.Apply(player, pid, hWnd, compiledFormat);
                    }
                }

                if (result != null)
                {
                    if (IsJunk(result.AnimeTitle, player))
                    {
                        continue;
                    }

                    result.ProcessName = procName;
                    result.Pid = pid;
                    return result;
                }
            }
        }

        return null;
    }

    public async Task<ParsedMedia?> DetectAsync()
    {
        var session = await DetectSessionAsync();
        return session.Media;
    }

    public HashSet<string> GetRunningPlayerNames()
    {
        return DetectSession().RunningPlayers;
    }

    private static bool IsJunk(string title, AnisthesiaPlayer player)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;

        var trimmed = title.Trim();

        // 1. Ignore if title is exactly the player name
        if (string.Equals(trimmed, player.Name, StringComparison.OrdinalIgnoreCase)) return true;

        // 2. Ignore common player "empty" states (O(1) FrozenSet lookup)
        if (JunkPatterns.Contains(trimmed)) return true;

        // 3. Ignore very short titles (probably noise)
        if (trimmed.Length < 2) return true;

        // 4. Ignore common system file names if they leaked through
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
