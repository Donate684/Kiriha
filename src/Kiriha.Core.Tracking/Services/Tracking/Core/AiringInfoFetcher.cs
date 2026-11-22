using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Core;

public class AiringInfoFetcher
{
    private readonly IAniListApiService _aniListApi;
    private readonly IShikiApiService? _shikiApi;
    private readonly ISettingsService? _settingsService;

    public AiringInfoFetcher(
        IAniListApiService aniListApi,
        IShikiApiService? shikiApi = null,
        ISettingsService? settingsService = null)
    {
        _aniListApi = aniListApi;
        _shikiApi = shikiApi;
        _settingsService = settingsService;
    }

    public async Task<(EpisodeAiringInfo? Airing, int AiredCount, DateTime? NextSlot)> FetchAndResolveAsync(AnimeEntity anime, bool force, CancellationToken ct)
    {
        var airing = await FetchAsync(anime.Id, force, ct);
        if (airing is null || airing.IsStaleFallback) return (airing, anime.EpisodesAired, anime.NextEpisodeAt);

        var (aired, nextSlot) = ResolveAired(anime, airing);
        return (airing, aired, nextSlot);
    }

    internal Task<EpisodeAiringInfo?> FetchAsync(int id, bool force, CancellationToken ct)
    {
        var source = _settingsService?.Current.System.AiringSource ?? EpisodeAiringSource.AniList;
        return source == EpisodeAiringSource.Shikimori && _shikiApi != null
            ? _shikiApi.GetAiringInfoAsync(id, force, ct)
            : _aniListApi.GetNextAiringAsync(id, force, ct);
    }

    public static (int aired, DateTime? nextSlot) ResolveAired(AnimeEntity anime, EpisodeAiringInfo airing)
    {
        if (airing.IsStaleFallback) return (anime.EpisodesAired, anime.NextEpisodeAt);

        bool finished = AppConstants.AiringStatus.IsFinishedAiring(airing.Status);
        int total = Math.Max(anime.TotalEpisodes, airing.TotalEpisodes.GetValueOrDefault());
        // The provider confirms release by reporting an aired count or advancing the next episode.
        int confirmed = finished && total > 0
            ? total
            : airing.EpisodesAired ?? (airing.NextEpisode is > 0 ? airing.NextEpisode.Value - 1 : 0);
        if (total > 0) confirmed = Math.Min(total, confirmed);
        int aired = Math.Max(anime.EpisodesAired, confirmed);
        DateTime? nextSlot = finished
            ? null
            : airing.NextEpisodeAt ?? (aired == anime.EpisodesAired ? anime.NextEpisodeAt : null);
        return (aired, nextSlot);
    }
}
