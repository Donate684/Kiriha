using System.Collections.Concurrent;
using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Infrastructure.Platform;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

/// <summary>
/// Lightweight file-backed <see cref="IPlaybackResumeStore"/> used exclusively in standalone
/// <c>--player</c> CLI mode (where the SQLite persistence layer is intentionally not loaded).
/// In Main App mode, <c>SqlitePlaybackResumeStore</c> is registered instead.
/// </summary>
public sealed class FilePlaybackResumeStore : IPlaybackResumeStore
{
    private const int MaxEntries = 500;

    public sealed record PlaybackResumeEntry(double Position, double Duration, DateTime UpdatedAt);

    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, PlaybackResumeEntry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _fileLock = new();
    private bool _isLoaded;

    public FilePlaybackResumeStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(PathHelper.GetSettingsDirPath(), AppConstants.System.FileNames.PlaybackResume);
    }

    private void EnsureLoaded()
    {
        if (_isLoaded) return;
        lock (_fileLock)
        {
            if (_isLoaded) return;
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, PlaybackResumeEntry>>(json);
                    if (data != null)
                    {
                        foreach (var kvp in data)
                            _entries[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to load playback resume store from {FilePath}", _filePath);
            }
            finally
            {
                _isLoaded = true;
            }
        }
    }

    public void SavePosition(string url, double position, double duration)
    {
        if (string.IsNullOrWhiteSpace(url) || position < 5) return;
        EnsureLoaded();

        _entries[url] = new PlaybackResumeEntry(position, duration, DateTime.UtcNow);
        PruneIfNeeded();
        SaveToFile();
    }

    public double? TryGetResumePosition(string url, string? watchLaterDir = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        EnsureLoaded();

        if (_entries.TryGetValue(url, out var entry))
        {
            if (entry.Position >= 5 && (entry.Duration <= 0 || entry.Position < entry.Duration * 0.95))
            {
                return entry.Position;
            }
        }

        if (!string.IsNullOrWhiteSpace(watchLaterDir))
        {
            return WatchLaterHelper.TryGetSavedPosition(watchLaterDir, url);
        }

        return null;
    }

    public void Remove(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        EnsureLoaded();

        if (_entries.TryRemove(url, out _))
        {
            SaveToFile();
        }
    }

    private void PruneIfNeeded()
    {
        if (_entries.Count <= MaxEntries)
            return;

        var oldest = _entries
            .OrderBy(kvp => kvp.Value.UpdatedAt)
            .Take(_entries.Count - MaxEntries)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldest)
        {
            _entries.TryRemove(key, out _);
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

                var snapshot = _entries.ToArray().ToDictionary(k => k.Key, v => v.Value);
                var json = JsonSerializer.Serialize(snapshot);
                var tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(_filePath))
                    File.Replace(tmp, _filePath, null);
                else
                    File.Move(tmp, _filePath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to save playback resume store to {FilePath}", _filePath);
        }
    }
}
