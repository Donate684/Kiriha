using System.Net;
using System.Text;
using System.Text.Json;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class AniListApiService
{
    private const int AiringBatchSize = 25;
    private static readonly TimeSpan AiringBatchTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DefaultAiringBackoff = TimeSpan.FromMinutes(1);
    private long _nextAiringBatchUtcTicks;

    public async Task<IReadOnlyDictionary<int, EpisodeAiringInfo>> GetAiringSchedulesAsync(
        IReadOnlyList<int> malIds, CancellationToken ct = default)
    {
        var results = new Dictionary<int, EpisodeAiringInfo>();
        foreach (var ids in malIds.Where(id => id > 0).Distinct().Chunk(AiringBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            if (IsAiringBatchCoolingDown()) break;
            var batch = await RequestAiringBatchAsync(ids, ct);
            if (batch is null) break;

            foreach (var (id, airing) in batch)
            {
                await WriteCacheAsync(CacheKey(id), airing, ct);
                results.Add(id, airing);
            }
        }

        ct.ThrowIfCancellationRequested();
        return results;
    }

    private async Task<Dictionary<int, EpisodeAiringInfo>?> RequestAiringBatchAsync(int[] ids, CancellationToken ct)
    {
        const string query = """
            query ($malIds: [Int], $count: Int!) {
              Page(page: 1, perPage: $count) {
                media(idMal_in: $malIds, type: ANIME) {
                  id
                  idMal
                  status
                  episodes
                  nextAiringEpisode { episode airingAt }
                }
              }
            }
            """;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(AiringBatchTimeout);
        var token = deadline.Token;
        try
        {
            using var lease = await _rateLimiter.AcquireAsync(1, token);
            if (!lease.IsAcquired || IsAiringBatchCoolingDown()) return null;
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    query,
                    variables = new { malIds = ids, count = ids.Length }
                }, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Options.Set(ResilientHttpHandler.ReturnRateLimitResponse, true);
            request.Headers.Add("User-Agent", AppInfo.UserAgent);
            using var response = await _httpClient.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var now = _clock.GetUtcNow();
                var retry = response.Headers.RetryAfter;
                var until = retry?.Date ?? now + (retry?.Delta ?? DefaultAiringBackoff);
                Interlocked.Exchange(ref _nextAiringBatchUtcTicks, until.UtcTicks);
                return null;
            }
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            return ParseAiringBatch(document.RootElement, ids);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Debug(ex, "AniList: batch airing request failed");
            return null;
        }
    }

    private bool IsAiringBatchCoolingDown() =>
        _clock.GetUtcNow().UtcTicks < Interlocked.Read(ref _nextAiringBatchUtcTicks);

    private static Dictionary<int, EpisodeAiringInfo> ParseAiringBatch(JsonElement root, int[] ids)
    {
        var results = new Dictionary<int, EpisodeAiringInfo>();
        if (!TryGetObjectProperty(root, "data", out var data) ||
            !TryGetObjectProperty(data, "Page", out var page) ||
            !page.TryGetProperty("media", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return results;

        var erroredRows = GetErroredRows(root);
        if (erroredRows is null) return results;

        var requested = ids.ToHashSet();
        foreach (var (index, item) in entries.EnumerateArray().Index())
        {
            if (erroredRows.Contains(index) || !TryGetRequestedMalId(item, requested, out var id)) continue;
            var airing = AniListParser.ParseAiringMedia(item, id);
            if (airing.SourceId > 0 && !string.IsNullOrWhiteSpace(airing.Status) && HasValidEpisodeSlot(item, airing))
                results.TryAdd(id, airing);
        }
        return results;
    }

    private static bool HasValidEpisodeSlot(JsonElement item, EpisodeAiringInfo airing) =>
        item.TryGetProperty("nextAiringEpisode", out var next) &&
        (next.ValueKind == JsonValueKind.Null || airing is { NextEpisode: > 0, NextEpisodeAt: not null });

    private static bool TryGetRequestedMalId(JsonElement item, HashSet<int> requested, out int id)
    {
        id = 0;
        return item.ValueKind == JsonValueKind.Object &&
               item.TryGetProperty("idMal", out var property) && property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out id) && requested.Contains(id);
    }

    private static HashSet<int>? GetErroredRows(JsonElement root)
    {
        HashSet<int> indices = [];
        if (!root.TryGetProperty("errors", out var errors)) return indices;
        if (errors.ValueKind != JsonValueKind.Array) return null;
        foreach (var error in errors.EnumerateArray())
        {
            if (error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("path", out var path) ||
                path.ValueKind != JsonValueKind.Array || path.GetArrayLength() < 3 ||
                path[0].ValueKind != JsonValueKind.String || !path[0].ValueEquals("Page") ||
                path[1].ValueKind != JsonValueKind.String || !path[1].ValueEquals("media") ||
                path[2].ValueKind != JsonValueKind.Number || !path[2].TryGetInt32(out var index) || index < 0)
                return null;

            indices.Add(index);
        }
        return indices;
    }

    private static bool TryGetObjectProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) &&
               value.ValueKind == JsonValueKind.Object;
    }
}
