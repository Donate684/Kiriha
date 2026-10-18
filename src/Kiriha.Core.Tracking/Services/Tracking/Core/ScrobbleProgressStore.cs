using System.Collections.Concurrent;
using System.Text.Json;
using Kiriha.Infrastructure.Platform;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public class ScrobbleProgressStore : IScrobbleProgressStore
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, int> _progress = new();
    private readonly Lock _fileLock = new();

    public ScrobbleProgressStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(PathHelper.GetSettingsDirPath(), "scrobble_progress.json");
        Load();
    }

    public int GetElapsed(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        return _progress.TryGetValue(key, out var elapsed) ? Math.Max(0, elapsed) : 0;
    }

    public void SaveElapsed(string key, int elapsed)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _progress[key] = Math.Max(0, elapsed);
        SaveToFile();
    }

    public void Clear(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (_progress.TryRemove(key, out _))
        {
            SaveToFile();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        _progress[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to load scrobble progress from {FilePath}", _filePath);
        }
    }

    private void SaveToFile()
    {
        try
        {
            lock (_fileLock)
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var json = JsonSerializer.Serialize(_progress.ToDictionary(k => k.Key, v => v.Value));
                File.WriteAllText(_filePath, json);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to save scrobble progress to {FilePath}", _filePath);
        }
    }
}