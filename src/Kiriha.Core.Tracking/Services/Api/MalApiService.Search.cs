using System.Text.Json;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class MalApiService
{
    private static readonly TimeSpan UpdatingDetailsTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan FinishedDetailsTtl = TimeSpan.FromDays(14);

    public async Task<List<AnimeEntity>> SearchAnimeAsync(string query, CancellationToken ct = default) =>
        (await SearchAnimeCatalogAsync(query, ct)).Items;

    internal async Task<(List<AnimeEntity> Items, bool Validated)> SearchAnimeCatalogAsync(string query, CancellationToken ct = default)
    {
        var list = new List<AnimeEntity>();
        var url = $"anime?q={Uri.EscapeDataString(query)}&limit=50&fields={AnimeCatalogFields}&nsfw=true";
        try
        {
            var response = await GetCatalogResponseAsync(url, ct, getFreshness: CatalogSearchCachePolicy.Mal);
            if (response.Body is null) return (list, false);

            using var json = JsonDocument.Parse(response.Body);

            if (!json.RootElement.TryGetProperty("data", out var data)) return (list, false);
            foreach (var node in data.EnumerateArray()) list.Add(MalMapper.MapJsonToAnimeEntity(node.GetProperty("node")));
            return (list, !response.IsStaleFallback);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: SearchAnimeAsync failed"); return ([], false); }
    }

    public async Task<List<AnimeEntity>> SearchMangaAsync(string query, CancellationToken ct = default)
    {
        var list = new List<AnimeEntity>();
        var url = $"manga?q={Uri.EscapeDataString(query)}&limit=50&fields={MangaCatalogFields}&nsfw=true";
        try
        {
            var bytes = (await GetCatalogResponseAsync(url, ct, getFreshness: CatalogSearchCachePolicy.Mal)).Body;
            if (bytes is null) return list;

            using var json = JsonDocument.Parse(bytes);

            if (json.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var node in data.EnumerateArray()) list.Add(MalMapper.MapJsonToAnimeEntity(node.GetProperty("node")));
            }
            return list;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: SearchMangaAsync failed"); return list; }
    }

    public Task<AnimeEntity?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default) =>
        GetDetailsAsync(animeId, MediaKind.Anime, onlyChanged: false, ct);

    public Task<AnimeEntity?> GetMangaDetailsAsync(int mangaId, CancellationToken ct = default) =>
        GetDetailsAsync(mangaId, MediaKind.Manga, onlyChanged: false, ct);

    public Task<AnimeEntity?> RefreshCachedDetailsAsync(int id, MediaKind kind, CancellationToken ct = default) =>
        GetDetailsAsync(id, kind, onlyChanged: true, ct);

    public async Task<(AnimeEntity? Details, bool IsFresh)> ReadCachedDetailsAsync(int id, MediaKind kind, CancellationToken ct = default)
    {
        try
        {
            var (bytes, age) = await _httpCache.ReadCachedResponseAsync(GetCatalogUrl(DetailsUrl(id, kind)), ct);
            if (bytes is null) return (null, false);
            using var json = JsonDocument.Parse(bytes);
            if (!IsExpectedTitle(json.RootElement, id)) return (null, false);
            return (MalMapper.MapJsonToAnimeEntity(json.RootElement), age < GetDetailsTtl(json.RootElement));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug(ex, "MalApiService: unable to read cached details for {Kind} {Id}", kind, id);
            return (null, false);
        }
    }

    private async Task<AnimeEntity?> GetDetailsAsync(int id, MediaKind kind, bool onlyChanged, CancellationToken ct)
    {
        try
        {
            var response = await GetCatalogResponseAsync(DetailsUrl(id, kind), ct, getFreshness: body => GetCachedDetailsTtl(body, id));
            if (response.Body is null || onlyChanged &&
                (response.IsStaleFallback || !response.BodyChanged && response.StatusCode.HasValue))
                return null;

            using var json = JsonDocument.Parse(response.Body);
            return MalMapper.MapJsonToAnimeEntity(json.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "MalApiService: details lookup failed for {Kind} {Id}", kind, id);
            return null;
        }
    }

    private static string DetailsUrl(int id, MediaKind kind) => kind == MediaKind.Anime
        ? $"anime/{id}?fields={AnimeCatalogFields}&nsfw=true"
        : $"manga/{id}?fields={MangaCatalogFields}&nsfw=true";

    private static TimeSpan? GetCachedDetailsTtl(byte[] body, int id)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return IsExpectedTitle(json.RootElement, id) ? GetDetailsTtl(json.RootElement) : null;
        }
        catch (JsonException ex)
        {
            Log.Debug(ex, "MalApiService: invalid catalog details in cache");
            return null;
        }
    }

    private static bool IsExpectedTitle(JsonElement root, int id) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("id", out var key) && key.ValueKind == JsonValueKind.Number &&
        key.TryGetInt32(out var value) && value == id &&
        root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(title.GetString());

    private static TimeSpan GetDetailsTtl(JsonElement root) =>
        root.TryGetProperty("status", out var status) &&
        status.ValueKind == JsonValueKind.String &&
        status.GetString() is "finished_airing" or "finished"
            ? FinishedDetailsTtl
            : UpdatingDetailsTtl;
}
