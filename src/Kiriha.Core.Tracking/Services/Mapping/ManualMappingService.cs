using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Parsing;
using Serilog;

namespace Kiriha.Core.Tracking.Mapping;

public class ManualMappingService
{
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Sentinel value indicating the title was explicitly unlinked by the user
    /// and must not be auto-matched.
    /// </summary>
    public const int NegativeMappingId = -1;

    private readonly ITitleMappingRepository? _repository;
    private readonly string? _legacyMappingFilePath;
    private readonly ConcurrentDictionary<string, int> _manualMappings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> _pendingWrites = new();
    private readonly Lock _initLock = new();
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ManualMappingService(ITitleMappingRepository? repository = null, string? legacyMappingFilePath = null)
    {
        _repository = repository;
        _legacyMappingFilePath = legacyMappingFilePath;
    }

    public bool TryGetMapping(string normalizedTitle, out int malId)
    {
        EnsureInitialized();
        var key = AnimeStringHelper.Normalize(normalizedTitle);
        if (_manualMappings.TryGetValue(key, out malId) && malId > 0)
            return true;
        malId = 0;
        return false;
    }

    public bool IsNegativelyMapped(string normalizedTitle)
    {
        EnsureInitialized();
        var key = AnimeStringHelper.Normalize(normalizedTitle);
        return _manualMappings.TryGetValue(key, out int id) && id == NegativeMappingId;
    }

    public void AddMapping(string title, int malId)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string normalized = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrEmpty(normalized)) return;

        EnsureInitialized();
        _manualMappings[normalized] = malId;
        if (_repository != null)
        {
            TrackWrite(_repository.UpsertAsync(new TitleMappingEntity
            {
                NormalizedTitle = normalized,
                TargetId = malId,
                MediaKind = MediaKind.Anime
            }));
        }
        Log.Information("ManualMappingService: Added manual mapping: {Title} ({Normalized}) -> {Id}", title, normalized, malId);
    }

    public void AddNegativeMapping(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string normalized = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrEmpty(normalized)) return;

        EnsureInitialized();
        _manualMappings[normalized] = NegativeMappingId;
        if (_repository != null)
        {
            TrackWrite(_repository.UpsertAsync(new TitleMappingEntity
            {
                NormalizedTitle = normalized,
                TargetId = NegativeMappingId,
                MediaKind = MediaKind.Anime
            }));
        }
        Log.Information("ManualMappingService: Added negative mapping for: {Title} ({Normalized})", title, normalized);
    }

    public bool RemoveMapping(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        string normalized = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrEmpty(normalized)) return false;

        EnsureInitialized();
        if (_manualMappings.TryRemove(normalized, out _))
        {
            if (_repository != null)
            {
                TrackWrite(_repository.DeleteAsync(normalized));
            }
            Log.Information("ManualMappingService: Removed manual mapping for: {Title} ({Normalized})", title, normalized);
            return true;
        }
        return false;
    }

    public async Task FlushAsync()
    {
        EnsureInitialized();
        var pending = _pendingWrites.Keys.ToArray();
        if (pending.Length == 0)
            return;

        try
        {
            await Task.WhenAll(pending);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ManualMappingService: error while flushing pending SQLite writes");
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            try
            {
                if (_repository != null)
                {
                    var stored = Task.Run(() => _repository.GetAllAsync()).GetAwaiter().GetResult();
                    foreach (var row in stored)
                    {
                        _manualMappings[row.NormalizedTitle] = row.TargetId;
                    }
                }

                MigrateLegacyJsonIfNeeded();
                _initialized = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ManualMappingService: failed initial hydration");
            }
        }
    }

    private void MigrateLegacyJsonIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(_legacyMappingFilePath) || !File.Exists(_legacyMappingFilePath))
            return;

        try
        {
            var json = File.ReadAllText(_legacyMappingFilePath);
            var doc = JsonSerializer.Deserialize<TitleMappingsDocument>(json, JsonOptions);
            if (doc is { Version: CurrentSchemaVersion, Mappings.Count: > 0 })
            {
                var toPersist = new List<TitleMappingEntity>();
                foreach (var (key, targetId) in doc.Mappings)
                {
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (_manualMappings.TryAdd(key, targetId))
                    {
                        toPersist.Add(new TitleMappingEntity
                        {
                            NormalizedTitle = key,
                            TargetId = targetId,
                            MediaKind = MediaKind.Anime
                        });
                    }
                }

                if (toPersist.Count > 0 && _repository != null)
                {
                    Task.Run(() => _repository.UpsertBatchAsync(toPersist)).GetAwaiter().GetResult();
                }
            }

            Log.Information("ManualMappingService: migrated and deleted legacy {Path}", _legacyMappingFilePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ManualMappingService: failed migrating legacy mappings from {Path}", _legacyMappingFilePath);
        }
        finally
        {
            TryDeleteLegacyFile(_legacyMappingFilePath);
        }
    }

    private readonly record struct TitleMappingsDocument
    {
        [JsonPropertyName("version")]
        public int Version { get; init; }

        [JsonPropertyName("mappings")]
        public Dictionary<string, int>? Mappings { get; init; }
    }

    private static void TryDeleteLegacyFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ManualMappingService: failed to delete legacy mapping file {Path}", path);
        }
    }

    private void TrackWrite(Task task)
    {
        if (task.IsCompleted)
            return;

        _pendingWrites.TryAdd(task, 0);
        task.ContinueWith(
            t => _pendingWrites.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
