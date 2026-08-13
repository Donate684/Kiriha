using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Models;
using Kiriha.Core.Repositories;
using Kiriha.Core.Services;
using Kiriha.Core.Abstractions.Models.Entities;

namespace Kiriha.Core.Tracking.Core;

public class AiringInfoFetcher
{
    private readonly IAniListApiService _aniListApi;

    public AiringInfoFetcher(IAniListApiService aniListApi)
    {
        _aniListApi = aniListApi;
    }

    public async Task<(AniListAiringInfo? Airing, int AiredCount, DateTime? NextSlot)> FetchAndResolveAsync(AnimeEntity anime, bool force, CancellationToken ct)
    {
        var airing = await _aniListApi.GetNextAiringAsync(anime.Id, force, ct);
        if (airing == null) return (null, anime.EpisodesAired, anime.NextEpisodeAt);

        var (aired, nextSlot) = ResolveAired(anime, airing);
        return (airing, aired, nextSlot);
    }

    public static (int aired, DateTime? nextSlot) ResolveAired(AnimeEntity anime, AniListAiringInfo airing)
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
        else if (airing.Status == "FINISHED")
        {
            if (airing.TotalEpisodes.HasValue && airing.TotalEpisodes > 0)
                aired = airing.TotalEpisodes.Value;
            else if (anime.TotalEpisodes > 0)
                aired = anime.TotalEpisodes;
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
