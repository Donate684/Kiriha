using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Tracking.Anisthesia.Strategies;

namespace Kiriha.Infrastructure.Tracking.Anisthesia;

public class DetectionManager
{
    private readonly List<AnisthesiaPlayer> _players;
    private readonly ISettingsService _settingsService;
    private readonly FrozenDictionary<string, List<AnisthesiaPlayer>> _exactExecutableMap;
    private readonly (Regex Regex, AnisthesiaPlayer Player, List<AnisthesiaPlayer> PlayerList)[] _regexExecutableRules;

    private static readonly FrozenSet<string> JunkPatterns = FrozenSet.ToFrozenSet(
    [
        "vlc media player", "mpc-hc", "potplayer", "mpv", "kmplayer", "zoom player", "ready", "opening..."
    ], StringComparer.OrdinalIgnoreCase);

    public DetectionManager(List<AnisthesiaPlayer> players, ISettingsService settingsService)
    {
        _players = players;
        _settingsService = settingsService;

        var exactDict = new Dictionary<string, List<AnisthesiaPlayer>>(StringComparer.OrdinalIgnoreCase);
        var regexList = new List<(Regex, AnisthesiaPlayer, List<AnisthesiaPlayer>)>();

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
        }

        _exactExecutableMap = exactDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _regexExecutableRules = regexList.ToArray();
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
                    if (matchingPlayers == null)
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
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return (running, null);

        var allowedProcesses = _settingsService.Current?.System?.Scrobbler?.AllowedProcesses;
        var hasFilter = allowedProcesses != null && allowedProcesses.Count > 0;
        ParsedMedia? detectedMedia = null;

        // Fast low-allocation path: query Windows toolhelp snapshot without allocating Process objects
        if (WindowsProcessSnapshot.TryEnumerateProcesses(_processBuffer))
        {
            for (int p = 0; p < _processBuffer.Count; p++)
            {
                var (pid, procName) = _processBuffer[p];
                try
                {
                    var matchingPlayers = GetMatchingPlayers(procName);
                    if (matchingPlayers == null || matchingPlayers.Count == 0) continue;

                    for (int i = 0; i < matchingPlayers.Count; i++)
                    {
                        running.Add(matchingPlayers[i].Name);
                    }

                    if (detectedMedia != null) continue;

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

                        ParsedMedia? result = null;
                        foreach (var strategy in player.Strategies)
                        {
                            if (strategy == StrategyType.OpenFiles)
                            {
                                result = HandleEnumerationStrategy.Apply(player, pid);
                            }
                            else if (strategy == StrategyType.WindowTitle)
                            {
                                if (!hWndEvaluated)
                                {
                                    try
                                    {
                                        using var pObj = Process.GetProcessById((int)pid);
                                        hWnd = pObj.MainWindowHandle;
                                    }
                                    catch { }
                                    hWndEvaluated = true;
                                }
                                if (hWnd != IntPtr.Zero)
                                {
                                    result = WindowTitleStrategy.Apply(player, pid, hWnd);
                                }
                            }

                            if (result != null)
                            {
                                if (IsJunk(result.AnimeTitle, player))
                                {
                                    result = null;
                                    continue;
                                }
                                result.ProcessName = procName;
                                result.Pid = pid;
                                detectedMedia = result;
                                break;
                            }
                        }

                        if (detectedMedia != null) break;
                    }
                }
                catch { /* Access denied or process exited */ }
            }

            return (running, detectedMedia);
        }

        // Fallback for non-Windows or when Toolhelp32 snapshot fails
        var processes = Process.GetProcesses();
        try
        {
            foreach (var proc in processes)
            {
                try
                {
                    string procName = proc.ProcessName;
                    var matchingPlayers = GetMatchingPlayers(procName);
                    if (matchingPlayers == null || matchingPlayers.Count == 0) continue;

                    for (int i = 0; i < matchingPlayers.Count; i++)
                    {
                        running.Add(matchingPlayers[i].Name);
                    }

                    if (detectedMedia != null) continue;

                    uint pid = (uint)proc.Id;
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

                        ParsedMedia? result = null;
                        foreach (var strategy in player.Strategies)
                        {
                            if (strategy == StrategyType.OpenFiles)
                            {
                                result = HandleEnumerationStrategy.Apply(player, pid);
                            }
                            else if (strategy == StrategyType.WindowTitle)
                            {
                                if (!hWndEvaluated)
                                {
                                    hWnd = proc.MainWindowHandle;
                                    hWndEvaluated = true;
                                }
                                if (hWnd != IntPtr.Zero)
                                {
                                    result = WindowTitleStrategy.Apply(player, pid, hWnd);
                                }
                            }

                            if (result != null)
                            {
                                if (IsJunk(result.AnimeTitle, player))
                                {
                                    result = null;
                                    continue;
                                }
                                result.ProcessName = procName;
                                result.Pid = pid;
                                detectedMedia = result;
                                break;
                            }
                        }

                        if (detectedMedia != null) break;
                    }
                }
                catch { /* Access denied or process exited */ }
            }
        }
        finally
        {
            for (int i = 0; i < processes.Length; i++)
            {
                processes[i].Dispose();
            }
        }

        return (running, detectedMedia);
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



