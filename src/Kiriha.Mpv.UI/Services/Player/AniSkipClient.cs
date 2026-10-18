using Kiriha.Core.Domain.Constants;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

public interface IAniSkipClient
{
    Task<List<SkipSegment>> GetSkipSegmentsAsync(int malId, int episode, double durationSeconds, CancellationToken cancellationToken = default);
}

public sealed class AniSkipClient : IAniSkipClient
{
    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly ConcurrentDictionary<(int malId, int episode), List<SkipSegment>> s_cache = new();

    static AniSkipClient()
    {
        s_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Kiriha/1.0 ({AppConstants.Links.GitHubRepo})");
        s_httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<List<SkipSegment>> GetSkipSegmentsAsync(int malId, int episode, double durationSeconds, CancellationToken cancellationToken = default)
    {
        if (malId <= 0 || episode <= 0)
            return new List<SkipSegment>();

        var key = (malId, episode);
        if (s_cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var epLength = durationSeconds > 0 ? durationSeconds : 0.0;
            var url = $"{AppConstants.Api.AniSkip.BaseUrl}{malId}/{episode}?types=op&types=ed&types=mixed-op&types=mixed-ed&types=recap&episodeLength={epLength.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}";

            var response = await s_httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound && epLength > 0)
            {
                response.Dispose();
                var fallbackUrl = $"{AppConstants.Api.AniSkip.BaseUrl}{malId}/{episode}?types=op&types=ed&types=mixed-op&types=mixed-ed&types=recap&episodeLength=0";
                response = await s_httpClient.GetAsync(fallbackUrl, cancellationToken).ConfigureAwait(false);
            }

            using (response)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var empty = new List<SkipSegment>();
                    s_cache[key] = empty;
                    return empty;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Log.Debug("AniSkip returned HTTP {Code} for MAL {MalId} ep {Episode}", response.StatusCode, malId, episode);
                    return new List<SkipSegment>();
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new List<SkipSegment>();

            var payload = JsonSerializer.Deserialize<AniSkipResponse>(json);
            if (payload == null || !payload.Found || payload.Results == null)
            {
                var empty = new List<SkipSegment>();
                s_cache[key] = empty;
                return empty;
            }

            var segments = new List<SkipSegment>();
            foreach (var r in payload.Results)
            {
                if (r.Interval == null)
                    continue;

                var type = r.SkipType switch
                {
                    "op" => SkipSegmentType.Intro,
                    "ed" => SkipSegmentType.Outro,
                    "mixed-op" => SkipSegmentType.MixedIntro,
                    "mixed-ed" => SkipSegmentType.MixedOutro,
                    "recap" => SkipSegmentType.Recap,
                    _ => (SkipSegmentType?)null
                };

                if (!type.HasValue)
                    continue;

                var title = type.Value switch
                {
                    SkipSegmentType.Intro => "Opening",
                    SkipSegmentType.Outro => "Ending",
                    SkipSegmentType.Recap => "Recap",
                    _ => "Skip"
                };

                var start = Math.Max(0, r.Interval.StartTime);
                var end = durationSeconds > 0 ? Math.Min(r.Interval.EndTime, durationSeconds) : r.Interval.EndTime;

                if (end > start)
                {
                    segments.Add(new SkipSegment(type.Value, start, end, title, "aniskip"));
                }
            }

            s_cache[key] = segments;
                return segments;
            }
        }
        catch (OperationCanceledException)
        {
            return new List<SkipSegment>();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to fetch AniSkip markers for MAL {MalId} ep {Episode}", malId, episode);
            return new List<SkipSegment>();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes")]
    private sealed class AniSkipResponse
    {
        [JsonPropertyName("found")]
        public bool Found { get; set; }

        [JsonPropertyName("results")]
        public List<AniSkipResult>? Results { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes")]
    private sealed class AniSkipResult
    {
        [JsonPropertyName("skipType")]
        public string? SkipType { get; set; }

        [JsonPropertyName("interval")]
        public AniSkipInterval? Interval { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes")]
    private sealed class AniSkipInterval
    {
        [JsonPropertyName("startTime")]
        public double StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public double EndTime { get; set; }
    }
}