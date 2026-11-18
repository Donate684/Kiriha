using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Api;

public partial class MalApiService
{
    public async Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default)
    {
        var values = new List<KeyValuePair<string, string>> { new("num_watched_episodes", episodes.ToString()) };
        if (score.HasValue && score.Value > 0) values.Add(new("score", score.Value.ToString()));
        if (status != null && status != UserAnimeStatus.None)
        {
            var malStatus = StatusMapper.ToMal(status.Value);
            if (!string.IsNullOrEmpty(malStatus))
                values.Add(new("status", malStatus));
        }
        if (isRewatching.HasValue && isRewatching.Value) values.Add(new("is_rewatching", "true"));
        if (rewatchCount.HasValue && rewatchCount.Value > 0) values.Add(new("num_times_rewatched", rewatchCount.Value.ToString()));

        return await SendPatchAsync($"anime/{animeId}/my_list_status", values, ct);
    }

    public async Task<SyncOutcome> SaveFullListStatusAsync(AnimeEntity item, CancellationToken ct = default)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;

        var values = new List<KeyValuePair<string, string>>
        {
            new(isManga ? "num_chapters_read" : "num_watched_episodes", isManga ? item.ChaptersRead.ToString() : item.Progress.ToString())
        };

        var malStatus = StatusMapper.ToMal(item.Status, isManga);
        if (!string.IsNullOrEmpty(malStatus))
        {
            values.Add(new("status", malStatus));
        }

        if (isManga)
        {
            if (item.VolumesRead > 0)
                values.Add(new("num_volumes_read", item.VolumesRead.ToString()));
            if (item.RewatchCount > 0)
                values.Add(new("num_times_reread", item.RewatchCount.ToString()));
            if (item.IsRewatching)
                values.Add(new("is_rereading", "true"));
        }
        else
        {
            if (item.RewatchCount > 0)
                values.Add(new("num_times_rewatched", item.RewatchCount.ToString()));
            if (item.IsRewatching)
                values.Add(new("is_rewatching", "true"));
        }

        int scoreVal = int.TryParse(item.Score, out int s) && s > 0 ? s : 0;
        values.Add(new("score", scoreVal.ToString()));

        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            values.Add(new("comments", item.Notes));
        }

        if (item.DateStarted.HasValue)
        {
            values.Add(new("start_date", item.DateStarted.Value.ToString("yyyy-MM-dd")));
        }

        if (item.DateCompleted.HasValue)
        {
            values.Add(new("finish_date", item.DateCompleted.Value.ToString("yyyy-MM-dd")));
        }

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
        return MapStatusToOutcome(statusCode.Value);
    }

    public async Task<SyncOutcome> UpdateMangaProgressAsync(int mangaId, int chapters, int? volumes = null, UserAnimeStatus? status = null, int? score = null, CancellationToken ct = default)
    {
        var values = new List<KeyValuePair<string, string>> { new("num_chapters_read", chapters.ToString()) };
        if (volumes.HasValue && volumes.Value > 0) values.Add(new("num_volumes_read", volumes.Value.ToString()));
        if (score.HasValue && score.Value > 0) values.Add(new("score", score.Value.ToString()));
        if (status != null && status != UserAnimeStatus.None)
        {
            var malStatus = StatusMapper.ToMal(status.Value, true);
            if (!string.IsNullOrEmpty(malStatus))
                values.Add(new("status", malStatus));
        }

        return await SendPatchAsync($"manga/{mangaId}/my_list_status", values, ct);
    }
}
