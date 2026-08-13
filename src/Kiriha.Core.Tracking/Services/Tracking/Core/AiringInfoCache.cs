using Kiriha.Models.Entities;
using Kiriha.Core.Repositories;
using Kiriha.Core.Services;
using System;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Models;
using Kiriha.Models.Entities;
using Kiriha.Core.Repositories;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public class AiringInfoCache
{
    private readonly IAnimeRepository _animeRepo;
    private readonly INotificationService _notificationService;
    private readonly IUiDispatcher _uiDispatcher;

    public AiringInfoCache(
        IAnimeRepository animeRepo,
        INotificationService notificationService,
        IUiDispatcher uiDispatcher)
    {
        _animeRepo = animeRepo;
        _notificationService = notificationService;
        _uiDispatcher = uiDispatcher;
    }

    public async Task ApplyAndSaveAiringAsync(AnimeEntity anime, int finalAiredCount, DateTime? nextSlot, DateTime now)
    {
        int? notifyEp = null;

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (finalAiredCount != anime.EpisodesAired)
            {
                bool isFirstSyncJumpFromZero = anime.LastEpisodesSync == null && anime.EpisodesAired == 0;

                if (!isFirstSyncJumpFromZero && finalAiredCount > anime.EpisodesAired)
                {
                    anime.LastEpisodeAt = now;
                    notifyEp = finalAiredCount;
                }

                anime.EpisodesAired = finalAiredCount;
                anime.AiredSourcePriority = 2;
            }

            anime.NextEpisodeAt = nextSlot;
            anime.LastEpisodesSync = now;
            // anime.RefreshMetadata();
        });

        if (notifyEp.HasValue)
        {
            Log.Information("AiringInfoService: New episode detected for {Title}: {Count}", anime.Title, notifyEp.Value);
            _notificationService.NotifyNewEpisode(anime, notifyEp.Value);
        }

        await _animeRepo.AddOrUpdateAnimeAsync(anime);
    }

    public async Task MarkSyncedAsync(AnimeEntity anime, DateTime now)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            anime.LastEpisodesSync = now;
            // anime.RefreshMetadata();
        });
        await _animeRepo.AddOrUpdateAnimeAsync(anime);
    }
}
