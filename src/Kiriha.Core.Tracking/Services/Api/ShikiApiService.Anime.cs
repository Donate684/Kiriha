using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class ShikiApiService
{
    public Task<List<AnimeEntity>> SearchAnimeAsync(string query, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AnimeEntity>());
    }

    public Task<AnimeEntity?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default)
    {
        return Task.FromResult<AnimeEntity?>(null);
    }

    public async Task<EpisodeAiringInfo?> GetAiringInfoAsync(int malId, bool force = false, CancellationToken ct = default)
    {
        if (malId <= 0) return null;

        try
        {
            var result = await _httpCache.SendForResultAsync(
                requestFactory: _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, ShikiBaseUrl + $"animes/{malId}")),
                throttle: innerCt => _rateLimiter.ThrottleAsync(innerCt),
                ct: ct,
                localTtl: force ? TimeSpan.Zero : TimeSpan.FromHours(6));

            if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Log.Warning("Shikimori: anime {MalId} not found (404)", malId);
                return null;
            }

            if (result.Body == null || result.Body.Length == 0)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(result.Body);
            return ShikiParser.ParseAiringInfo(doc.RootElement, malId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shikimori: failed to fetch next airing for MAL {MalId}", malId);
            return null;
        }
    }

    public Task<List<AnimeEntity>> SearchMangaAsync(string query, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AnimeEntity>());
    }

    public Task<AnimeEntity?> GetMangaDetailsAsync(int mangaId, CancellationToken ct = default)
    {
        return Task.FromResult<AnimeEntity?>(null);
    }

    public async Task<ShikiFranchiseResponse?> GetFranchiseAsync(int animeId, CancellationToken ct = default)
    {
        var bytes = await _httpCache.SendAsync(
            requestFactory: _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, ShikiBaseUrl + $"animes/{animeId}/franchise")),
            ct: ct,
            localTtl: TimeSpan.FromDays(30));

        if (bytes == null) return null;

        try
        {
            return JsonSerializer.Deserialize<ShikiFranchiseResponse>(bytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService: failed to deserialize franchise for {AnimeId}", animeId);
            return null;
        }
    }

    public async Task<ShikiPersonResponse?> GetPersonWorksAsync(int personId, CancellationToken ct = default)
    {
        if (_personCache.TryGetValue(personId, out var hit) && (DateTime.UtcNow - hit.SystemDateTime) < TimeSpan.FromHours(1))
        {
            return hit.Value;
        }

        var bytes = await _httpCache.SendAsync(
            requestFactory: _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, ShikiBaseUrl + $"people/{personId}")),
            ct: ct,
            localTtl: TimeSpan.FromDays(30));

        if (bytes == null) return null;

        try
        {
            var result = JsonSerializer.Deserialize<ShikiPersonResponse>(bytes);
            _personCache[personId] = (result, DateTime.UtcNow);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService: failed to deserialize person data for {PersonId}", personId);
            return null;
        }
    }
}
