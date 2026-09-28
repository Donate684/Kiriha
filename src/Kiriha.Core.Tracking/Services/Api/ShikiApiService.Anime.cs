using System.Text.Json;

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

            if (result.Body is null || result.Body.Length == 0)
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

        if (bytes is null) return null;

        try
        {
            var res = JsonSerializer.Deserialize<ShikiFranchiseResponse>(bytes);
            if (res != null)
            {
                NormalizeFranchiseResponse(res);
            }
            return res;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService: failed to deserialize franchise for {AnimeId}", animeId);
            return null;
        }
    }

    private void NormalizeFranchiseResponse(ShikiFranchiseResponse res)
    {
        var mirror = _settingsService.Current.Api.ShikiMirror;
        string root;
        if (mirror == ShikiMirror.Net && !string.IsNullOrEmpty(_hostResolver.ActiveForkHost))
        {
            root = $"{Uri.UriSchemeHttps}{Uri.SchemeDelimiter}{_hostResolver.ActiveForkHost}";
        }
        else if (mirror == ShikiMirror.One && !string.IsNullOrEmpty(_hostResolver.ActiveOriginalHost))
        {
            root = $"{Uri.UriSchemeHttps}{Uri.SchemeDelimiter}{_hostResolver.ActiveOriginalHost}";
        }
        else
        {
            var host = ShikiEndpoints.Host(mirror);
            root = host.BaseUrl;
            int apiIdx = root.IndexOf("/api", StringComparison.OrdinalIgnoreCase);
            if (apiIdx >= 0) root = root.Substring(0, apiIdx);
        }
        root = root.TrimEnd('/');

        foreach (var node in res.Nodes)
        {
            if (!string.IsNullOrEmpty(node.ImageUrl) && node.ImageUrl.StartsWith('/'))
            {
                node.ImageUrl = root + node.ImageUrl;
            }
            if (!string.IsNullOrEmpty(node.Url) && node.Url.StartsWith('/'))
            {
                node.Url = root + node.Url;
            }
        }
    }
}
