using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
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

    public async Task ApplyAndSaveAiringAsync(
        AnimeEntity anime,
        int finalAiredCount,
        DateTime? nextSlot,
        DateTime now,
        int? totalEpisodes = null,
        string? airingStatus = null)
    {
        int? notifyEp = null;

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (finalAiredCount != anime.EpisodesAired)
            {
                bool isFirstSyncJumpFromZero = anime.LastEpisodesSync is null && anime.EpisodesAired == 0;

                if (!isFirstSyncJumpFromZero && finalAiredCount > anime.EpisodesAired)
                {
                    anime.LastEpisodeAt = now;
                    notifyEp = finalAiredCount;
                }

                anime.EpisodesAired = finalAiredCount;
                anime.AiredSourcePriority = 2;
            }

            if (totalEpisodes.HasValue && totalEpisodes.Value > 0 && (anime.TotalEpisodes == 0 || anime.TotalEpisodes < totalEpisodes.Value))
            {
                anime.TotalEpisodes = totalEpisodes.Value;
            }

            if (!string.IsNullOrEmpty(airingStatus) && string.IsNullOrEmpty(anime.StatusDetailed))
            {
                anime.StatusDetailed = airingStatus.ToUpperInvariant() switch
                {
                    "RELEASING" => AppConstants.AiringStatus.CurrentlyAiring,
                    "NOT_YET_RELEASED" => AppConstants.AiringStatus.NotYetAired,
                    "FINISHED" => AppConstants.AiringStatus.FinishedAiring,
                    _ => airingStatus
                };
            }

            anime.NextEpisodeAt = nextSlot;
            anime.LastEpisodesSync = now;
            anime.RefreshAiringBadge();
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
            anime.RefreshAiringBadge();
        });
        await _animeRepo.AddOrUpdateAnimeAsync(anime);
    }
}
