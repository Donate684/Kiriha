using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Services.Tracking;
using Kiriha.Core.Domain.Constants;
using Serilog;

namespace Kiriha.Core.Tracking.Services.Api;

public class MalHistoryDeepParserService : IMalHistoryDeepParserService
{
    private readonly HttpClient _httpClient;

    public MalHistoryDeepParserService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(DateTime? StartDate, DateTime? EndDate)?> FetchLatestWatchDatesAsync(int malId, string cookie, bool isManga = false, CancellationToken ct = default)
    {
        if (malId <= 0 || string.IsNullOrWhiteSpace(cookie))
        {
            return null;
        }

        var cookieHeader = cookie.Trim();
        if (!cookieHeader.Contains("MALSESSIONID=", StringComparison.OrdinalIgnoreCase))
        {
            cookieHeader = $"MALSESSIONID={cookieHeader}";
        }
        if (!cookieHeader.Contains("is_logged_in=", StringComparison.OrdinalIgnoreCase))
        {
            cookieHeader = $"{cookieHeader}; is_logged_in=1";
        }

        var idParam = isManga ? $"detailedmid={malId}" : $"detailedaid={malId}";
        var url = $"{AppConstants.Api.Mal.AjaxHistoryUrl}?keepThis=true&{idParam}&TB_iframe=true&height=420&width=390";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.Headers.Add("Referer", isManga ? AppConstants.Api.Mal.MangaListUrl : AppConstants.Api.Mal.AnimeListUrl);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("MalHistoryDeepParser: Received {StatusCode} for anime {MalId}", response.StatusCode, malId);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            if (html.Contains(AppConstants.Api.Mal.NotLoggedIn, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("MalHistoryDeepParser: Received 'Not logged in' from MAL for anime {MalId}", malId);
                throw new UnauthorizedAccessException("MAL returned 'Not logged in'. The session cookie is invalid or expired.");
            }

            var result = MalHistoryParser.Parse(html);
            if (result == null)
            {
                return null;
            }

            return (result.LatestStartDate, result.LatestEndDate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MalHistoryDeepParser: Failed to fetch history for anime {MalId}", malId);
            return null;
        }
    }
}
