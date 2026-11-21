using System.Text.Json;
using System.Text.Json.Serialization;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;
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

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITitleMappingRepository? _repository;
    private readonly string? _legacyMappingFilePath;
    private readonly DeferredKeyedState<int> _state;

    public ManualMappingService(ITitleMappingRepository? repository = null, string? legacyMappingFilePath = null)
    {
        _repository = repository;
        _legacyMappingFilePath = legacyMappingFilePath;
        _state = new(() => Task.Run(LoadAsync).GetAwaiter().GetResult(), repository == null ? null : PersistAsync,
            ex => Log.Warning(ex, "Failed to load or persist manual title mappings"));
    }

    public bool TryGetMapping(string normalizedTitle, out int malId)
    {
        var key = AnimeStringHelper.Normalize(normalizedTitle);
        if (_state.TryGet(key, out malId) && malId > 0)
            return true;
        malId = 0;
        return false;
    }

    public bool IsNegativelyMapped(string normalizedTitle)
    {
        var key = AnimeStringHelper.Normalize(normalizedTitle);
        return _state.TryGet(key, out int id) && id == NegativeMappingId;
    }

    public void AddMapping(string title, int malId)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string normalized = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrEmpty(normalized)) return;

        _state.Set(normalized, malId);
        Log.Information("ManualMappingService: Added manual mapping: {Title} ({Normalized}) -> {Id}", title, normalized, malId);
    }

    public void AddNegativeMapping(string title) => AddMapping(title, NegativeMappingId);

    public bool RemoveMapping(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        string normalized = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrEmpty(normalized)) return false;

        return _state.Remove(normalized, loadExisting: true);
    }

    public Task FlushAsync(CancellationToken ct = default) => _state.FlushAsync(ct);

    private async Task<Dictionary<string, int>> LoadAsync()
    {
        var repository = _repository;
        var mappings = repository == null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : (await repository.GetAllAsync()).ToDictionary(
                row => row.NormalizedTitle, row => row.TargetId, StringComparer.Ordinal);

        if (_legacyMappingFilePath is not { Length: > 0 } path || !File.Exists(path))
            return mappings;

        var document = await ReadLegacyAsync(path);
        if (document is not { Version: CurrentSchemaVersion, Mappings: { } entries })
        {
            DeleteInvalidLegacyFile(path);
            return mappings;
        }

        var imported = new List<TitleMappingEntity>();
        foreach (var (key, targetId) in entries)
        {
            if (!string.IsNullOrWhiteSpace(key) && mappings.TryAdd(key, targetId))
                imported.Add(CreateMapping(key, targetId));
        }

        if (repository != null)
        {
            await repository.UpsertBatchAsync(imported);
            File.Delete(path);
        }

        return mappings;
    }

    private static async Task<TitleMappingsDocument?> ReadLegacyAsync(string path)
    {
        try
        {
            await using var input = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<TitleMappingsDocument>(input, JsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Discarding invalid legacy title mappings in {Path}", path);
            return null;
        }
    }

    private static void DeleteInvalidLegacyFile(string path)
    {
        try
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to discard invalid legacy title mapping file {Path}", path);
        }
    }

    private Task PersistAsync(string key, int? targetId)
    {
        var repository = _repository ?? throw new InvalidOperationException("Title mapping storage is unavailable.");
        return targetId is { } id ? repository.UpsertAsync(CreateMapping(key, id)) : repository.DeleteAsync(key);
    }

    private static TitleMappingEntity CreateMapping(string key, int targetId) =>
        new() { NormalizedTitle = key, TargetId = targetId, MediaKind = MediaKind.Anime };

    private readonly record struct TitleMappingsDocument
    {
        [JsonPropertyName("version")]
        public int Version { get; init; }

        [JsonPropertyName("mappings")]
        public Dictionary<string, int>? Mappings { get; init; }
    }
}
