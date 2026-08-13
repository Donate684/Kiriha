using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class MalApiService
{
    public async Task<List<AnimeEntity>> SearchAnimeAsync(string query, CancellationToken ct = default)
    {
        var list = new List<AnimeEntity>();
        var url = $"anime?q={Uri.EscapeDataString(query)}&limit=50&fields={AnimeFields}&nsfw=true";
        try
        {
            var bytes = await GetWithCacheAsync(url, ct);
            if (bytes == null) return list;

            using var json = JsonDocument.Parse(bytes);

            if (json.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var node in data.EnumerateArray()) list.Add(MalMapper.MapJsonToAnimeEntity(node.GetProperty("node")));
            }
            return list;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: SearchAnimeAsync failed"); return list; }
    }

    public async Task<AnimeEntity?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default)
    {
        try
        {
            var bytes = await GetWithCacheAsync($"anime/{animeId}?fields={AnimeFields}&nsfw=true", ct, localTtl: TimeSpan.FromDays(30));
            if (bytes == null) return null;

            using var json = JsonDocument.Parse(bytes);
            return MalMapper.MapJsonToAnimeEntity(json.RootElement);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: GetAnimeDetailsAsync failed for {Id}", animeId); return null; }
    }

    public async Task<List<AnimeEntity>> SearchMangaAsync(string query, CancellationToken ct = default)
    {
        var list = new List<AnimeEntity>();
        var url = $"manga?q={Uri.EscapeDataString(query)}&limit=50&fields={MangaFields}&nsfw=true";
        try
        {
            var bytes = await GetWithCacheAsync(url, ct);
            if (bytes == null) return list;

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

    public async Task<AnimeEntity?> GetMangaDetailsAsync(int mangaId, CancellationToken ct = default)
    {
        try
        {
            var bytes = await GetWithCacheAsync($"manga/{mangaId}?fields={MangaFields}&nsfw=true", ct, localTtl: TimeSpan.FromDays(30));
            if (bytes == null) return null;

            using var json = JsonDocument.Parse(bytes);
            return MalMapper.MapJsonToAnimeEntity(json.RootElement);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: GetMangaDetailsAsync failed for {Id}", mangaId); return null; }
    }

    public Task<List<EpisodeRelease>> GetEpisodeListAsync(int malId, CancellationToken ct = default) =>
        _jikanApi.GetEpisodeListAsync(malId, ct);

    public Task<int?> GetLatestEpisodeFromForumAsync(int malId, CancellationToken ct = default) =>
        _jikanApi.GetLatestEpisodeFromForumAsync(malId, ct);
}
