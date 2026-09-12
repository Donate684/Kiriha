using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Core;

public class AiringInfoFetcher
{
    private readonly IAniListApiService _aniListApi;
    private readonly IShikiApiService? _shikiApi;
    private readonly ISettingsService? _settingsService;

    public AiringInfoFetcher(IAniListApiService aniListApi)
    {
        _aniListApi = aniListApi;
    }

    public AiringInfoFetcher(IAniListApiService aniListApi, IShikiApiService shikiApi, ISettingsService settingsService)
    {
        _aniListApi = aniListApi;
        _shikiApi = shikiApi;
        _settingsService = settingsService;
    }

    public async Task<(EpisodeAiringInfo? Airing, int AiredCount, DateTime? NextSlot)> FetchAndResolveAsync(AnimeEntity anime, bool force, CancellationToken ct)
    {
        EpisodeAiringInfo? airing = null;
        var source = _settingsService?.Current.System.AiringSource ?? EpisodeAiringSource.AniList;

        if (source == EpisodeAiringSource.Shikimori && _shikiApi != null)
        {
            airing = await _shikiApi.GetAiringInfoAsync(anime.Id, force, ct);
        }
        else
        {
            var aniAiring = await _aniListApi.GetNextAiringAsync(anime.Id, force, ct);
            airing = aniAiring?.ToEpisodeAiringInfo();
        }

        if (airing is null) return (null, anime.EpisodesAired, anime.NextEpisodeAt);

        var (aired, nextSlot) = ResolveAired(anime, airing);
        return (airing, aired, nextSlot);
    }

    public static (int aired, DateTime? nextSlot) ResolveAired(AnimeEntity anime, AniListAiringInfo airing) =>
        ResolveAired(anime, airing.ToEpisodeAiringInfo());

    public static (int aired, DateTime? nextSlot) ResolveAired(AnimeEntity anime, EpisodeAiringInfo airing)
    {
        int aired = anime.EpisodesAired;
        DateTime? nextSlot = airing.NextEpisodeAt;

        if (airing.NextEpisode.HasValue)
        {
            if (airing.NextEpisodeAt.HasValue && airing.NextEpisodeAt.Value <= DateTime.UtcNow)
            {
                aired = airing.NextEpisode.Value;
                nextSlot = null;
            }
            else
            {
                aired = Math.Max(0, airing.NextEpisode.Value - 1);
            }
        }
        else if (airing.Status is "FINISHED" or "released")
        {
            if (airing.TotalEpisodes.HasValue && airing.TotalEpisodes > 0)
                aired = airing.TotalEpisodes.Value;
            else if (anime.TotalEpisodes > 0)
                aired = anime.TotalEpisodes;
            else if (airing.EpisodesAired.HasValue && airing.EpisodesAired > 0)
                aired = airing.EpisodesAired.Value;
        }
        else if (anime.NextEpisodeAt.HasValue && anime.NextEpisodeAt.Value <= DateTime.UtcNow)
        {
            aired = Math.Max(aired, anime.EpisodesAired + 1);
        }

        if (anime.TotalEpisodes > 0 && aired > anime.TotalEpisodes)
            aired = anime.TotalEpisodes;

        return (aired, nextSlot);
    }
}
