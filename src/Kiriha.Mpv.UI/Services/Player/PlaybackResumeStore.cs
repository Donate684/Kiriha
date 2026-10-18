using System.Collections.Concurrent;
using System.Text.Json;
using Kiriha.Infrastructure.Platform;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

public static class PlaybackResumeStore
{
    private static readonly string FilePath = Path.Combine(PathHelper.GetSettingsDirPath(), "playback_resume.json");
    private static readonly ConcurrentDictionary<string, PlaybackResumeEntry> s_entries = new();
    private static readonly Lock s_fileLock = new();
    private static bool s_isLoaded;

    public record PlaybackResumeEntry(double Position, double Duration, DateTime UpdatedAt);

    private static void EnsureLoaded()
    {
        if (s_isLoaded) return;
        lock (s_fileLock)
        {
            if (s_isLoaded) return;
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, PlaybackResumeEntry>>(json);
                    if (data != null)
                    {
                        foreach (var kvp in data)
                            s_entries[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to load playback resume store from {FilePath}", FilePath);
            }
            finally
            {
                s_isLoaded = true;
            }
        }
    }

    public static void SavePosition(string url, double position, double duration)
    {
        if (string.IsNullOrWhiteSpace(url) || position < 5) return;
        EnsureLoaded();

        s_entries[url] = new PlaybackResumeEntry(position, duration, DateTime.UtcNow);
        SaveToFile();
    }

    public static double? TryGetResumePosition(string url, string watchLaterDir)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        EnsureLoaded();

        if (s_entries.TryGetValue(url, out var entry))
        {
            if (entry.Position >= 5 && (entry.Duration <= 0 || entry.Position < entry.Duration * 0.95))
            {
                return entry.Position;
            }
        }

        return WatchLaterHelper.TryGetSavedPosition(watchLaterDir, url);
    }

    public static void Remove(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        EnsureLoaded();

        if (s_entries.TryRemove(url, out _))
        {
            SaveToFile();
        }
    }

    private static void SaveToFile()
    {
        try
        {
            lock (s_fileLock)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var json = JsonSerializer.Serialize(s_entries.ToDictionary(k => k.Key, v => v.Value));
                File.WriteAllText(FilePath, json);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to save playback resume store to {FilePath}", FilePath);
        }
    }
}