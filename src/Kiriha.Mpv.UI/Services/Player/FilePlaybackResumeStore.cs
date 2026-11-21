using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Utils;
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
    private readonly Dictionary<string, PlaybackResumeEntry?> _pending = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public FilePlaybackResumeStore(string? filePath = null)
    {
        _filePath = Path.GetFullPath(
            filePath ?? Path.Combine(PathHelper.GetSettingsDirPath(), AppConstants.System.FileNames.PlaybackResume));
    }

    public void SavePosition(string url, double position, double duration)
    {
        if (!string.IsNullOrWhiteSpace(url) && double.IsFinite(position) && position >= 5 && double.IsFinite(duration))
            ApplyChange(url, new PlaybackResumeEntry(position, duration, DateTime.UtcNow));
    }

    public double? TryGetResumePosition(string url, string? watchLaterDir = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var entry = ReadEntry(url);
        if (entry is { Position: >= 5 } && (entry.Duration <= 0 || entry.Position < entry.Duration * 0.95))
            return entry.Position;

        return string.IsNullOrWhiteSpace(watchLaterDir)
            ? null
            : WatchLaterHelper.TryGetSavedPosition(watchLaterDir, url);
    }

    public void Remove(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
            ApplyChange(url, null);
    }

    public Task FlushAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                PersistChanges();
            }
        }, ct);

    private PlaybackResumeEntry? ReadEntry(string url)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(url, out var pending))
                return pending;

            try
            {
                using var fileLock = StateFileLock.Acquire(_filePath);
                return ReadEntries().GetValueOrDefault(url);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to load playback resume store from {FilePath}", _filePath);
                return null;
            }
        }
    }

    private void ApplyChange(string url, PlaybackResumeEntry? entry)
    {
        lock (_gate)
        {
            _pending[url] = entry;
            try
            {
                PersistChanges();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save playback resume store to {FilePath}", _filePath);
            }
        }
    }

    private void PersistChanges()
    {
        if (_pending.Count == 0) return;

        using var fileLock = StateFileLock.Acquire(_filePath);
        string? backupPath = null;
        Dictionary<string, PlaybackResumeEntry?> entries;
        try
        {
            entries = ReadEntries();
        }
        catch (JsonException ex)
        {
            backupPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".invalid";
            Log.Warning(ex, "Rebuilding invalid playback resume file {Path} with a backup at {BackupPath}",
                _filePath, backupPath);
            entries = [];
        }

        bool changed = false;
        foreach (var (key, entry) in _pending)
        {
            if (entry == null)
                changed |= entries.Remove(key);
            else
            {
                entries[key] = entry;
                changed = true;
            }
        }

        if (changed)
        {
            foreach (var key in entries.OrderBy(pair => pair.Value?.UpdatedAt)
                         .Take(entries.Count - MaxEntries).Select(pair => pair.Key).ToArray())
            {
                entries.Remove(key);
            }
            ReplaceFile(entries, backupPath);
        }

        _pending.Clear();
    }

    private Dictionary<string, PlaybackResumeEntry?> ReadEntries()
    {
        try
        {
            using var input = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, PlaybackResumeEntry?>>(input) ?? [];
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    private void ReplaceFile(Dictionary<string, PlaybackResumeEntry?> entries, string? backupPath)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, entries);
                output.Flush(flushToDisk: true);
            }

            if (backupPath != null)
                File.Replace(tempPath, _filePath, backupPath);
            else
                File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch (IOException ex) { Log.Debug(ex, "Failed to delete temporary playback resume file {Path}", tempPath); }
            catch (UnauthorizedAccessException ex) { Log.Debug(ex, "Failed to delete temporary playback resume file {Path}", tempPath); }
        }
    }
}
