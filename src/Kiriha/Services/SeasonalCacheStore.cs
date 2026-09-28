using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Extensions;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Platform;
using Serilog;

namespace Kiriha.Services.Data.Core;

/// <summary>
/// Disk persistence for the seasonal anime cache.
///
/// Each (year, season) lives in its own JSON file under
/// <c>{BasePath}/seasonal_cache/{year}_{season}.json</c>. Atomic writes
/// (temp + File.Move) avoid torn files on crash. Files older than
/// <see cref="Ttl"/> are treated as missing and deleted on load.
///
/// Rationale: <see cref="Kiriha.ViewModels.SeasonalViewModel"/> already
/// implements stale-while-revalidate over an in-memory cache. Persisting that
/// cache across restarts gives a near-instant first paint of the seasonal
/// view — fresh data still flows in the background a beat later.
/// </summary>
public sealed class SeasonalCacheStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly string _root;
    // Per-key write serialization. Avoids overlapping writes for the same
    // season tearing each other (e.g. background refresh writing while the
    // user-triggered fetch also writes). File.Move is atomic, but two
    // concurrent renames into the same destination is a coin flip on NTFS.
    private readonly Dictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly Lock _writeLocksGate = new();
    private readonly ConcurrentDictionary<string, string> _lastSavedHashes = new(StringComparer.OrdinalIgnoreCase);

    public SeasonalCacheStore()
    {
        _root = PathHelper.GetSeasonalCachePath();
        try { Directory.CreateDirectory(_root); }
        catch (Exception ex) { Log.Warning(ex, "SeasonalCacheStore: failed to create cache directory"); }
    }

    /// <summary>
    /// Eagerly reads every non-expired cache file from disk. Called once at
    /// SeasonalViewModel construction; expected to complete in well under
    /// 100 ms for a few-dozen-file directory.
    /// </summary>
    public IReadOnlyList<(int Year, string Season, List<AnimeEntity> Items)> LoadAll()
    {
        var results = new List<(int, string, List<AnimeEntity>)>();
        if (!Directory.Exists(_root)) return results;

        var threshold = DateTime.UtcNow - Ttl;
        Span<byte> hashBytes = stackalloc byte[32];

        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!TryParseKey(name, out int year, out string season)) continue;

                var info = new FileInfo(file);
                // Past seasons never expire — historical releases are immutable.
                // Only current and future seasons can expire after TTL.
                if (IsCurrentOrFuture(year, season) && info.LastWriteTimeUtc < threshold)
                {
                    // Expired — best-effort delete so the directory doesn't
                    // accumulate stale ongoing/future seasons.
                    try { info.Delete(); } catch (Exception ex) { Log.Debug(ex, "Failed to delete expired seasonal cache file {File}", file); }
                    continue;
                }

                var fileBytes = File.ReadAllBytes(file);
                SHA256.HashData(fileBytes, hashBytes);
                _lastSavedHashes[MakeKey(year, season)] = Convert.ToHexString(hashBytes);

                var items = JsonSerializer.Deserialize<List<AnimeEntity>>(fileBytes, JsonOptions);
                if (items is null || items.Count == 0) continue;

                results.Add((year, season, items));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SeasonalCacheStore: failed to load {File}", file);
                // Corrupt file — drop it so we don't keep tripping on it.
                try { File.Delete(file); } catch (Exception delEx) { Log.Debug(delEx, "Failed to delete corrupt seasonal cache file {File}", file); }
            }
        }

        return results;
    }

    public async Task SaveAsync(int year, string season, IReadOnlyList<AnimeEntity> items)
    {
        if (items is null || items.Count == 0) return;
        if (string.IsNullOrEmpty(season)) return;

        string key = MakeKey(year, season);
        string finalPath = Path.Combine(_root, key + ".json");
        string tmpPath = finalPath + ".tmp";

        var gate = GetWriteLock(key);
        await gate.WaitAsync();
        try
        {
            // Run JSON serialization + I/O off the calling thread (likely UI).
            await Task.Run(() =>
            {
                try
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(items, JsonOptions);
                    Span<byte> hashBytes = stackalloc byte[32];
                    SHA256.HashData(bytes, hashBytes);
                    var hash = Convert.ToHexString(hashBytes);

                    if (_lastSavedHashes.TryGetValue(key, out var lastHash) && string.Equals(hash, lastHash, StringComparison.Ordinal))
                    {
                        Log.Debug("SeasonalCacheStore: cache unchanged for {Key}, skipped write", key);
                        return;
                    }

                    Directory.CreateDirectory(_root);
                    File.WriteAllBytes(tmpPath, bytes);
                    File.Move(tmpPath, finalPath, overwrite: true);
                    _lastSavedHashes[key] = hash;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "SeasonalCacheStore: failed to save {Key}", key);
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch (Exception delEx) { Log.Debug(delEx, "Failed to delete temp seasonal cache file {TmpPath}", tmpPath); }
                }
            });
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GetWriteLock(string key)
    {
        lock (_writeLocksGate)
        {
            if (!_writeLocks.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _writeLocks[key] = gate;
            }
            return gate;
        }
    }

    private static string MakeKey(int year, string season) =>
        $"{year}_{season.ToLowerInvariant()}";

    private static bool TryParseKey(string name, out int year, out string season)
    {
        year = 0; season = string.Empty;
        var idx = name.IndexOf('_');
        if (idx <= 0 || idx == name.Length - 1) return false;
        if (!int.TryParse(name.AsSpan(0, idx), out year)) return false;
        season = name[(idx + 1)..].UppercaseFirst();
        return true;
    }

    private static bool IsCurrentOrFuture(int year, string season)
    {
        int month = DateTime.UtcNow.Month;
        int clockYear = DateTime.UtcNow.Year;
        if (month == 12) clockYear++;

        string clockSeason = month switch
        {
            1 or 2 or 12 => "Winter",
            3 or 4 or 5 => "Spring",
            6 or 7 or 8 => "Summer",
            _ => "Fall"
        };

        if (year > clockYear) return true;
        if (year < clockYear) return false;

        return SeasonOrder(season) >= SeasonOrder(clockSeason);
    }

    private static int SeasonOrder(string season) => season?.ToLowerInvariant() switch
    {
        "winter" => 0,
        "spring" => 1,
        "summer" => 2,
        "fall" => 3,
        _ => 0
    };
}
