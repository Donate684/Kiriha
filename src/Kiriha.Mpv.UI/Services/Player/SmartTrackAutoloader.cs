using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kiriha.Mpv.UI.Services.Player;

/// <summary>
/// Scans the directory of the currently playing video and finds external subtitle and audio
/// tracks whose episode number matches the current video file.
/// Ported from mpv-smart-sub-autoload (main.lua).
/// </summary>
public static class SmartTrackAutoloader
{
    private static readonly FrozenSet<string> SubtitleExtensions = FrozenSet.ToFrozenSet(
        [
            ".ass", ".idx", ".lrc", ".mks", ".pgs", ".rt", ".sbv", ".scc", ".smi",
            ".srt", ".srv3", ".ssa", ".sub", ".sup", ".utf", ".vtt", ".ytt"
        ], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> AudioExtensions = FrozenSet.ToFrozenSet(
        [
            ".mka", ".flac", ".aac", ".mp3", ".ogg", ".opus", ".m4a", ".wav", ".ac3", ".dts"
        ], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> VideoExtensions = FrozenSet.ToFrozenSet(
        [
            ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv",
            ".ts", ".m2ts", ".mpg", ".mpeg", ".ogv", ".rmvb", ".y4m"
        ], StringComparer.OrdinalIgnoreCase);

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
        string videoFileWithoutExt = Path.GetFileNameWithoutExtension(videoFileName);

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

        var videoElements = Kiriha.Utils.Parsing.AnimeParseCache.Parse(videoFileName);
        string? videoTitle = videoElements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
        string? videoEpisode = videoElements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;

        var matchedSubs = new List<string>();
        var matchedAudio = new List<string>();

        foreach (var sub in subFiles)
        {
            if (IsTrackMatch(videoFileName, videoFileWithoutExt, videoTitle, videoEpisode, sub))
                matchedSubs.Add(Path.Combine(directory, sub));
        }

        foreach (var audio in audioFiles)
        {
            if (IsTrackMatch(videoFileName, videoFileWithoutExt, videoTitle, videoEpisode, audio))
                matchedAudio.Add(Path.Combine(directory, audio));
        }

        return new SmartTrackMatches(matchedSubs, matchedAudio);
    }

    private static bool IsTrackMatch(string videoFileName, string videoFileWithoutExt, string? videoTitle, string? videoEpisode, string trackFileName)
    {
        string trackFileWithoutExt = Path.GetFileNameWithoutExtension(trackFileName);
        
        // 1. Exact Name Match (stripping common language codes)
        // e.g. "Video 01.en" == "Video 01"
        string trackBaseName = trackFileWithoutExt;
        int lastDotIndex = trackBaseName.LastIndexOf('.');
        if (lastDotIndex > 0)
        {
            string langCode = trackBaseName.Substring(lastDotIndex + 1);
            if (langCode.Length >= 2 && langCode.Length <= 5)
            {
                trackBaseName = trackBaseName.Substring(0, lastDotIndex);
            }
        }

        if (trackBaseName.Equals(videoFileWithoutExt, StringComparison.OrdinalIgnoreCase))
            return true;
        if (trackFileWithoutExt.Equals(videoFileWithoutExt, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. Anitomy Match
        var trackElements = Kiriha.Utils.Parsing.AnimeParseCache.Parse(trackFileName);
        string? trackTitle = trackElements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
        string? trackEpisode = trackElements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;

        bool hasVideoTitle = !string.IsNullOrEmpty(videoTitle);
        bool hasTrackTitle = !string.IsNullOrEmpty(trackTitle);
        bool hasVideoEpisode = !string.IsNullOrEmpty(videoEpisode);
        bool hasTrackEpisode = !string.IsNullOrEmpty(trackEpisode);

        if (hasVideoTitle && hasTrackTitle)
        {
            if (videoTitle!.Equals(trackTitle, StringComparison.OrdinalIgnoreCase))
            {
                if (hasVideoEpisode && hasTrackEpisode)
                    return videoEpisode!.Equals(trackEpisode, StringComparison.OrdinalIgnoreCase);
                else
                    return !hasVideoEpisode && !hasTrackEpisode;
            }
            return false;
        }

        if (!hasTrackTitle && hasTrackEpisode && hasVideoEpisode)
        {
            return videoEpisode!.Equals(trackEpisode, StringComparison.OrdinalIgnoreCase);
        }

        // 3. Fallback to modified CalculateConfidence (higher threshold)
        float confidence = CalculateConfidence(videoFileName, trackFileName);
        return confidence >= 80f;
    }

    private static float CalculateConfidence(string videoFileName, string trackFileName)
    {
        string normV = NormalizeForConfidence(Path.GetFileNameWithoutExtension(videoFileName));
        string normT = NormalizeForConfidence(Path.GetFileNameWithoutExtension(trackFileName));

        if (normV == normT) return 100f;

        var wordsV = normV.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordsT = normT.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (wordsV.Length == 0 || wordsT.Length == 0) return 0f;

        int matchingWords = wordsT.Count(w => wordsV.Contains(w));
        
        float score1 = (matchingWords / (float)wordsT.Length) * 100f;
        float score2 = (matchingWords / (float)wordsV.Length) * 100f;
        float score = Math.Max(score1, score2);

        if (normT.Length > 5 && normV.Length > 5)
        {
            if (normT.Contains(normV) || normV.Contains(normT))
                score = Math.Max(score, 90f);
        }

        return score;
    }

    private static string NormalizeForConfidence(string input)
    {
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]))
                chars[i] = ' ';
        }
        return new string(chars).ToLowerInvariant();
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
