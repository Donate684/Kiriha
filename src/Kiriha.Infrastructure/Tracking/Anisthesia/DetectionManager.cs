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
    private readonly (Regex Regex, AnisthesiaPlayer Player)[] _regexExecutableRules;

    private static readonly FrozenSet<string> JunkPatterns = new[]
    {
        "vlc media player", "mpc-hc", "potplayer", "mpv", "kmplayer", "zoom player", "ready", "opening..."
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public DetectionManager(List<AnisthesiaPlayer> players, ISettingsService settingsService)
    {
        _players = players;
        _settingsService = settingsService;

        var exactDict = new Dictionary<string, List<AnisthesiaPlayer>>(StringComparer.OrdinalIgnoreCase);
        var regexList = new List<(Regex, AnisthesiaPlayer)>();

        foreach (var player in players)
        {
            foreach (var exe in player.Executables)
            {
                if (exe.StartsWith('^'))
                {
                    regexList.Add((new Regex(exe, RegexOptions.IgnoreCase | RegexOptions.Compiled), player));
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

    public Task<(HashSet<string> RunningPlayers, ParsedMedia? Media)> DetectSessionAsync()
    {
        return Task.FromResult(DetectSession());
    }

    public (HashSet<string> RunningPlayers, ParsedMedia? Media) DetectSession()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return (running, null);

        var processes = Process.GetProcesses();
        ParsedMedia? detectedMedia = null;

        try
        {
            var allowedProcesses = _settingsService.Current?.System?.Scrobbler?.AllowedProcesses;
            var hasFilter = allowedProcesses != null && allowedProcesses.Count > 0;

            foreach (var proc in processes)
            {
                try
                {
                    string procName = proc.ProcessName;
                    List<AnisthesiaPlayer>? matchingPlayers = null;

                    if (_exactExecutableMap.TryGetValue(procName, out var exactMatches))
                    {
                        matchingPlayers = exactMatches;
                    }
                    else if (_regexExecutableRules.Length > 0)
                    {
                        for (int r = 0; r < _regexExecutableRules.Length; r++)
                        {
                            if (_regexExecutableRules[r].Regex.IsMatch(procName))
                            {
                                matchingPlayers ??= new List<AnisthesiaPlayer>(1);
                                matchingPlayers.Add(_regexExecutableRules[r].Player);
                            }
                        }
                    }

                    if (matchingPlayers == null || matchingPlayers.Count == 0) continue;

                    for (int i = 0; i < matchingPlayers.Count; i++)
                    {
                        running.Add(matchingPlayers[i].Name);
                    }

                    if (detectedMedia != null)
                    {
                        // Media already detected, only collecting running player names
                        continue;
                    }

                    uint pid = (uint)proc.Id;
                    IntPtr hWnd = proc.MainWindowHandle;

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
                                result = HandleEnumerationStrategy.Apply(player, pid);
                            else if (strategy == StrategyType.WindowTitle && hWnd != IntPtr.Zero)
                                result = WindowTitleStrategy.Apply(player, pid, hWnd);

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



