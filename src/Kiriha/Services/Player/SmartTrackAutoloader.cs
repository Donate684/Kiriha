using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kiriha.Services.Player;

/// <summary>
/// Scans the directory of the currently playing video and finds external subtitle and audio
/// tracks whose episode number matches the current video file.
/// Ported from mpv-smart-sub-autoload (main.lua).
/// </summary>
public static class SmartTrackAutoloader
{
    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ass", ".idx", ".lrc", ".mks", ".pgs", ".rt", ".sbv", ".scc", ".smi",
        ".srt", ".srv3", ".ssa", ".sub", ".sup", ".utf", ".vtt", ".ytt"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mka", ".flac", ".aac", ".mp3", ".ogg", ".opus", ".m4a", ".wav", ".ac3", ".dts"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv",
        ".ts", ".m2ts", ".mpg", ".mpeg", ".ogv", ".rmvb", ".y4m"
    };

    /// <summary>
    /// Finds external subtitle and audio track paths that match the episode number of
    /// <paramref name="videoPath"/>. Returns empty lists if the feature should not load anything
    /// (e.g. streaming URL, single video in folder, no external tracks found).
    /// </summary>
    public static SmartTrackMatches FindMatchingTracks(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || videoPath.Contains("://"))
            return SmartTrackMatches.Empty;

        var directory = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return SmartTrackMatches.Empty;

        string videoFileName = Path.GetRelativePath(directory, videoPath);

        List<string> allFiles = new();
        try
        {
            foreach (var file in EnumerateFilesWithDepth(directory, 2))
            {
                allFiles.Add(Path.GetRelativePath(directory, file));
            }
        }
        catch
        {
            return SmartTrackMatches.Empty;
        }

        // Determine episode number from the video file list
        var videoFiles = allFiles
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        int? episodeNumber = ExtractEpisodeNumber(videoFileName, videoFiles);

        // If we cannot determine episode number and there are multiple videos, bail out
        if (episodeNumber == null && videoFiles.Count > 1)
            return SmartTrackMatches.Empty;

        // Collect external subs and audio from the same directory
        var subFiles = allFiles
            .Where(f => SubtitleExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var audioFiles = allFiles
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (subFiles.Count == 0 && audioFiles.Count == 0)
            return SmartTrackMatches.Empty;

        var matchedSubs = new List<string>();
        var matchedAudio = new List<string>();

        foreach (var sub in subFiles)
        {
            int? subEp = ExtractEpisodeNumber(sub, subFiles);
            if (episodeNumber == null || subEp == episodeNumber)
                matchedSubs.Add(Path.Combine(directory, sub));
        }

        foreach (var audio in audioFiles)
        {
            int? audioEp = ExtractEpisodeNumber(audio, audioFiles);
            if (episodeNumber == null || audioEp == episodeNumber)
                matchedAudio.Add(Path.Combine(directory, audio));
        }

        return new SmartTrackMatches(matchedSubs, matchedAudio);
    }

    /// <summary>
    /// Determines the episode number of <paramref name="file"/> by finding the first numeric
    /// component that differs from neighboring files in <paramref name="sortedFiles"/>.
    /// Mirrors the Lua episode_number function exactly.
    /// </summary>
    internal static int? ExtractEpisodeNumber(string file, IReadOnlyList<string> sortedFiles)
    {
        int idx = -1;
        for (int i = 0; i < sortedFiles.Count; i++)
        {
            if (sortedFiles[i] == file) { idx = i; break; }
        }
        if (idx < 0)
            return null;

        var numbers = ExtractNumbers(file);
        if (numbers.Count == 0)
            return null;

        // Compare forward neighbors first, then backward
        int? ep = CompareWithNeighbors(numbers, sortedFiles, idx + 1, sortedFiles.Count - 1, step: 1)
               ?? CompareWithNeighbors(numbers, sortedFiles, idx - 1, 0, step: -1);

        return ep;
    }

    private static int? CompareWithNeighbors(
        IReadOnlyList<int> numbers,
        IReadOnlyList<string> sortedFiles,
        int start, int end, int step)
    {
        for (int i = start; step > 0 ? i <= end : i >= end; i += step)
        {
            var otherNumbers = ExtractNumbers(sortedFiles[i]);
            int len = Math.Min(numbers.Count, otherNumbers.Count);
            for (int n = 0; n < len; n++)
            {
                if (numbers[n] != otherNumbers[n])
                    return numbers[n];
            }
        }
        return null;
    }

    private static List<int> ExtractNumbers(string str)
    {
        var result = new List<int>();
        int i = 0;
        while (i < str.Length)
        {
            if (char.IsDigit(str[i]))
            {
                int start = i;
                while (i < str.Length && char.IsDigit(str[i])) i++;
                if (int.TryParse(str.AsSpan(start, i - start), out int num))
                    result.Add(num);
            }
            else
            {
                i++;
            }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateFilesWithDepth(string path, int maxDepth)
    {
        var queue = new Queue<(string path, int depth)>();
        queue.Enqueue((path, 0));

        while (queue.Count > 0)
        {
            var (currentPath, depth) = queue.Dequeue();

            string[] files;
            try { files = Directory.GetFiles(currentPath); }
            catch { continue; }
            foreach (var f in files) yield return f;

            if (depth < maxDepth)
            {
                string[] dirs;
                try { dirs = Directory.GetDirectories(currentPath); }
                catch { continue; }
                foreach (var d in dirs) queue.Enqueue((d, depth + 1));
            }
        }
    }
}

/// <summary>Result of a smart track autoload scan.</summary>
public readonly struct SmartTrackMatches
{
    public static readonly SmartTrackMatches Empty = new([], []);

    public IReadOnlyList<string> SubtitlePaths { get; }
    public IReadOnlyList<string> AudioPaths { get; }

    public bool HasAny => SubtitlePaths.Count > 0 || AudioPaths.Count > 0;

    public SmartTrackMatches(IReadOnlyList<string> subtitlePaths, IReadOnlyList<string> audioPaths)
    {
        SubtitlePaths = subtitlePaths;
        AudioPaths = audioPaths;
    }
}
