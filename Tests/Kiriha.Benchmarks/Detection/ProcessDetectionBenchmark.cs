using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Tracking.Anisthesia;

namespace Kiriha.Benchmarks.Detection;

[MemoryDiagnoser]
[ShortRunJob]
public class ProcessDetectionBenchmark
{
    private readonly List<AnisthesiaPlayer> _players;
    private readonly string[] _simulatedProcesses;

    // Optimized index structures:
    private readonly FrozenDictionary<string, AnisthesiaPlayer> _exactMap;
    private readonly (Regex Regex, AnisthesiaPlayer Player)[] _regexRules;

    public ProcessDetectionBenchmark()
    {
        _players = AnisthesiaPlayerLoader.Load().ToList();

        // 300 processes typical for Windows desktop
        var procList = new List<string>(300);
        string[] common = { "svchost", "chrome", "code", "devenv", "explorer", "discord", "spotify", "steam", "taskhostw", "dwm" };
        for (int i = 0; i < 280; i++)
        {
            procList.Add(common[i % common.Length] + (i / common.Length));
        }

        // Add actual media players in the mix
        procList.Add("mpc-hc64");
        procList.Add("vlc");
        procList.Add("mpv");
        procList.Add("PotPlayerMini64");
        while (procList.Count < 300) procList.Add("runtimebroker");

        _simulatedProcesses = procList.ToArray();

        // Precompute exact map & regexes
        var exactDict = new Dictionary<string, AnisthesiaPlayer>(StringComparer.OrdinalIgnoreCase);
        var regexList = new List<(Regex, AnisthesiaPlayer)>();

        foreach (var player in _players)
        {
            foreach (var exe in player.Executables)
            {
                if (exe.StartsWith('^'))
                {
                    regexList.Add((new Regex(exe, RegexOptions.IgnoreCase | RegexOptions.Compiled), player));
                }
                else
                {
                    exactDict.TryAdd(exe, player);
                }
            }
        }

        _exactMap = exactDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _regexRules = regexList.ToArray();
    }

    [Benchmark(Baseline = true)]
    public List<string> CurrentLinqMatching()
    {
        var running = new List<string>();

        foreach (var player in _players)
        {
            if (player.Executables.Any(exe =>
                _simulatedProcesses.Any(pn => pn.Equals(exe, StringComparison.OrdinalIgnoreCase)) ||
                (exe.StartsWith('^') && _simulatedProcesses.Any(pn => Regex.IsMatch(pn, exe, RegexOptions.IgnoreCase)))))
            {
                running.Add(player.Name);
            }
        }

        return running;
    }

    [Benchmark]
    public List<string> OptimizedIndexedMatching()
    {
        var runningSet = new HashSet<string>();

        foreach (var procName in _simulatedProcesses)
        {
            if (_exactMap.TryGetValue(procName, out var player))
            {
                runningSet.Add(player.Name);
            }
            else
            {
                for (int i = 0; i < _regexRules.Length; i++)
                {
                    if (_regexRules[i].Regex.IsMatch(procName))
                    {
                        runningSet.Add(_regexRules[i].Player.Name);
                        break;
                    }
                }
            }
        }

        return runningSet.ToList();
    }
}
