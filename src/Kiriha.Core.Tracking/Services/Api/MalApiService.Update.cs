using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Api;

public partial class MalApiService
{
    public async Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default)
    {
        var values = new List<KeyValuePair<string, string>> { new("num_watched_episodes", episodes.ToString()) };
        if (score.HasValue) values.Add(new("score", score.Value.ToString()));
        if (status != null && status != UserAnimeStatus.None) values.Add(new("status", StatusMapper.ToMal(status.Value)));
        if (isRewatching.HasValue) values.Add(new("is_rewatching", isRewatching.Value.ToString().ToLower()));
        if (rewatchCount.HasValue) values.Add(new("num_times_rewatched", rewatchCount.Value.ToString()));

        return await SendPatchAsync($"anime/{animeId}/my_list_status", values, ct);
    }

    public async Task<SyncOutcome> SaveFullListStatusAsync(AnimeEntity item, CancellationToken ct = default)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;

        var values = new List<KeyValuePair<string, string>>
        {
            new(isManga ? "num_chapters_read" : "num_watched_episodes", isManga ? item.ChaptersRead.ToString() : item.Progress.ToString()),
            new("status", StatusMapper.ToMal(item.Status, isManga)),
            new("num_times_rewatched", item.RewatchCount.ToString()),
            new("is_rewatching", item.IsRewatching.ToString().ToLower())
        };

        if (isManga)
        {
            values.Add(new("num_volumes_read", item.VolumesRead.ToString()));
        }

        int scoreVal = int.TryParse(item.Score, out int s) && s > 0 ? s : 0;
        values.Add(new("score", scoreVal.ToString()));
        values.Add(new("notes", item.Notes ?? string.Empty));
        values.Add(new("start_date", item.DateStarted.HasValue ? item.DateStarted.Value.ToString("yyyy-MM-dd") : string.Empty));
        values.Add(new("finish_date", item.DateCompleted.HasValue ? item.DateCompleted.Value.ToString("yyyy-MM-dd") : string.Empty));

        var endpoint = isManga ? $"manga/{item.Id}/my_list_status" : $"anime/{item.Id}/my_list_status";
        return await SendPatchAsync(endpoint, values, ct);
    }

    public Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default) =>
        RemoveEntryInternalAsync($"anime/{animeId}/my_list_status", ct);

    public Task<SyncOutcome> RemoveMangaAsync(int mangaId, CancellationToken ct = default) =>
        RemoveEntryInternalAsync($"manga/{mangaId}/my_list_status", ct);

    private async Task<SyncOutcome> RemoveEntryInternalAsync(string endpoint, CancellationToken ct)
    {
        var statusCode = await SendRequestRawAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, MalBaseUrl + endpoint),
            ct);

        if (statusCode is null) return SyncOutcome.TransientFailure;
        if ((int)statusCode.Value >= 200 && (int)statusCode.Value < 300) return SyncOutcome.Success;
        if (statusCode.Value == System.Net.HttpStatusCode.NotFound) return SyncOutcome.Success;
        if ((int)statusCode.Value >= 500 || statusCode.Value == System.Net.HttpStatusCode.RequestTimeout || statusCode.Value == System.Net.HttpStatusCode.TooManyRequests)
            return SyncOutcome.TransientFailure;
        return SyncOutcome.PermanentFailure;
    }

    public async Task<SyncOutcome> UpdateMangaProgressAsync(int mangaId, int chapters, int? volumes = null, UserAnimeStatus? status = null, int? score = null, CancellationToken ct = default)
    {
        var values = new List<KeyValuePair<string, string>> { new("num_chapters_read", chapters.ToString()) };
        if (volumes.HasValue) values.Add(new("num_volumes_read", volumes.Value.ToString()));
        if (score.HasValue) values.Add(new("score", score.Value.ToString()));
        if (status != null && status != UserAnimeStatus.None) values.Add(new("status", StatusMapper.ToMal(status.Value, true)));

        return await SendPatchAsync($"manga/{mangaId}/my_list_status", values, ct);
    }
}
