using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Kiriha.Core.Tracking.Api;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Tests;

public sealed partial class CacheReliabilityTests
{
    [Fact]
    public async Task AniListBatch_LeavesOmittedErroredAndMalformedRowsOutOfCache()
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        using var http = new HttpClient(new ScriptedHandler((_, _) => Json("""
            {"data":{"Page":{"media":[
              {"id":8,"idMal":42,"status":"RELEASING","episodes":12,"nextAiringEpisode":{"episode":5,"airingAt":1900000000}},
              {"id":9,"idMal":43,"status":"RELEASING","episodes":12,"nextAiringEpisode":null},
              {"id":10,"idMal":44,"status":"RELEASING","episodes":12,"nextAiringEpisode":{"episode":2,"airingAt":9223372036854775807}},
              {"id":11,"idMal":99,"status":"FINISHED","episodes":12,"nextAiringEpisode":null}
            ]}},"errors":[{"path":["Page","media",1,"nextAiringEpisode"],"message":"Unavailable"}]}
            """)));
        using var service = new AniListApiService(http, repo, Settings(), clock: clock);
        var result = await service.GetAiringSchedulesAsync([42, 43, 44, 45, 42]);
        Assert.Equal(42, Assert.Single(result).Key);
        Assert.Equal(5, result[42].NextEpisode);
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.HttpResponseCache.CountAsync());
    }

    [Fact]
    public async Task AniListBatch_UsesRetryAfterWithoutSendingRemainingGroups()
    {
        var (_, repo, clock) = await CreateCacheAsync();
        var sizes = new List<int>();
        var handler = new ScriptedHandler(async (request, ct) =>
        {
            using var doc = JsonDocument.Parse(await Assert.IsAssignableFrom<HttpContent>(request.Content).ReadAsStringAsync(ct));
            sizes.Add(doc.RootElement.GetProperty("variables").GetProperty("malIds").GetArrayLength());
            if (sizes.Count == 1)
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)) }
                };

            return await Json("""{"data":{"Page":{"media":[]}}}""");
        });
        using var http = new HttpClient(new ResilientHttpHandler(handler));
        using var service = new AniListApiService(http, repo, Settings(), clock: clock);
        Assert.Empty(await service.GetAiringSchedulesAsync(Enumerable.Range(1, 26).ToArray()));
        Assert.Equal([25], sizes);
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Empty(await service.GetAiringSchedulesAsync([42]));
        Assert.Equal([25], sizes);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(await service.GetAiringSchedulesAsync([42]));
        Assert.Equal([25, 1], sizes);
    }
}
