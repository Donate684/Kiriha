using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Platform;
using Serilog;

namespace Kiriha.Infrastructure.Caching;

/// <summary>
/// Disk persistence for the seasonal anime cache.
///
/// Each (year, season) lives in its own JSON file under
/// <c>{BasePath}/seasonal_cache/{year}_{season}.json</c>. Atomic writes
/// (temp + File.Move) avoid torn files on crash. The last-write timestamp
/// records when the catalog was validated so stale snapshots can be shown offline.
/// </summary>
public sealed class SeasonalCacheStore
{
    private static readonly TimeSpan CurrentFreshness = TimeSpan.FromHours(3);
    private static readonly TimeSpan UpcomingFreshness = TimeSpan.FromHours(12);
    private static readonly TimeSpan PastFreshness = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly string _root;
    private readonly TimeProvider _clock;

    // Per-key write serialization. Avoids overlapping writes for the same
    // season tearing each other (e.g. background refresh writing while the
    // user-triggered fetch also writes). File.Move is atomic, but two
    // concurrent renames into the same destination is a coin flip on NTFS.
    private readonly Dictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly Lock _writeLocksGate = new();
    private readonly ConcurrentDictionary<string, string> _lastSavedHashes = new(StringComparer.OrdinalIgnoreCase);

    public SeasonalCacheStore() : this(PathHelper.GetSeasonalCachePath(), TimeProvider.System)
    {
    }

    internal SeasonalCacheStore(string root, TimeProvider clock)
    {
        _root = root;
        _clock = clock;
        try { Directory.CreateDirectory(_root); }
        catch (Exception ex) { Log.Warning(ex, "SeasonalCacheStore: failed to create cache directory"); }
    }

    /// <summary>
    /// Eagerly reads every non-expired cache file from disk. Called once at
    /// SeasonalViewModel construction; expected to complete in well under
    /// 100 ms for a few-dozen-file directory.
    /// </summary>
    public IReadOnlyList<(int Year, string Season, List<AnimeEntity> Items, DateTime ValidatedAt)> LoadAll()
    {
        var results = new List<(int, string, List<AnimeEntity>, DateTime)>();
        if (!Directory.Exists(_root)) return results;

        Span<byte> hashBytes = stackalloc byte[32];

        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!TryParseKey(name, out int year, out string season)) continue;

                var info = new FileInfo(file);
                var fileBytes = File.ReadAllBytes(file);
                SHA256.HashData(fileBytes, hashBytes);
                _lastSavedHashes[MakeKey(year, season)] = Convert.ToHexString(hashBytes);

                var items = JsonSerializer.Deserialize<List<AnimeEntity>>(fileBytes, JsonOptions);
                if (items is null || items.Count == 0) continue;

                results.Add((year, season, items, info.LastWriteTimeUtc));
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

    public async Task SaveAsync(int year, string season, IReadOnlyList<AnimeEntity> items, bool validated = false,
        DateTime? catalogValidatedAt = null)
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

                    bool exists = File.Exists(finalPath);
                    var lastWrite = exists ? File.GetLastWriteTimeUtc(finalPath) : (DateTime?)null;
                    if (catalogValidatedAt is { } expected && lastWrite > expected)
                        return;

                    var timestamp = validated ? catalogValidatedAt ?? _clock.GetUtcNow().UtcDateTime : lastWrite ?? catalogValidatedAt;
                    if (exists && _lastSavedHashes.TryGetValue(key, out var lastHash) &&
                        string.Equals(hash, lastHash, StringComparison.Ordinal))
                    {
                        if (validated && timestamp is { } checkedAt && checkedAt > lastWrite)
                            File.SetLastWriteTimeUtc(finalPath, checkedAt);
                        Log.Debug("SeasonalCacheStore: cache unchanged for {Key}, skipped write", key);
                        return;
                    }

                    Directory.CreateDirectory(_root);
                    File.WriteAllBytes(tmpPath, bytes);
                    if ((validated || catalogValidatedAt.HasValue) && timestamp is { } snapshotTime)
                        File.SetLastWriteTimeUtc(tmpPath, snapshotTime);
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
        season = name[(idx + 1)..].ToLowerInvariant();
        return true;
    }

    public static (int Year, string Season) GetCurrentSeason(DateTime utcNow)
    {
        var season = utcNow.Month switch
        {
            1 or 2 or 12 => AppConstants.Seasons.Winter,
            3 or 4 or 5 => AppConstants.Seasons.Spring,
            6 or 7 or 8 => AppConstants.Seasons.Summer,
            _ => AppConstants.Seasons.Fall
        };
        return (utcNow.Month == 12 ? utcNow.Year + 1 : utcNow.Year, season);
    }

    public static TimeSpan GetFreshness(int year, string season, DateTime utcNow)
    {
        var current = GetCurrentSeason(utcNow);
        int offset = (year - current.Year) * 4 + SeasonOrder(season) - SeasonOrder(current.Season);
        return offset switch
        {
            > 0 => UpcomingFreshness,
            < 0 => PastFreshness,
            _ => CurrentFreshness
        };
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
