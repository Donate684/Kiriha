using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

internal sealed class AiringInfoCache(
    IAnimeRepository library,
    IUserAnimeRepository store,
    INotificationService notifications,
    IUiDispatcher dispatcher,
    LibrarySyncGate? gate)
{
    internal async Task SaveAsync(
        MediaKey key, long generation, EpisodeAiringInfo? airing, DateTime requestedAt, CancellationToken ct)
    {
        using var write = gate != null ? await gate.AcquireProgressWriteAsync(ct) : null;
        if (library.GetEntryGeneration(key.Id, key.Kind) != generation ||
            library.FindById(key.Id, key.Kind) is not { } current)
            return;

        var draft = await dispatcher.InvokeAsync(() => current.Clone());
        if (draft.Status != UserAnimeStatus.Watching || draft.LastEpisodesSync >= requestedAt)
            return;

        int? notifyEpisode = airing == null ? null : ApplyAiring(draft, airing, requestedAt);
        draft.LastEpisodesSync = requestedAt;
        if (!await store.UpdateAiringAsync(draft, ct)) return;

        await dispatcher.InvokeAsync(() =>
        {
            current.EpisodesAired = draft.EpisodesAired;
            current.NextEpisodeAt = draft.NextEpisodeAt;
            current.LastEpisodeAt = draft.LastEpisodeAt;
            current.LastEpisodesSync = draft.LastEpisodesSync;
            current.AiredSourcePriority = draft.AiredSourcePriority;
            current.StatusDetailed = draft.StatusDetailed;
            current.TotalEpisodes = draft.TotalEpisodes;
            current.RefreshAiringBadge();
        });

        if (notifyEpisode is { } episode)
        {
            Log.Information("AiringInfoService: New episode detected for {Title}: {Count}", current.Title, episode);
            notifications.NotifyNewEpisode(current, episode);
        }
    }

    private static int? ApplyAiring(AnimeEntity draft, EpisodeAiringInfo airing, DateTime requestedAt)
    {
        var (aired, nextSlot) = AiringInfoFetcher.ResolveAired(draft, airing);
        int? notifyEpisode = null;
        if (aired != draft.EpisodesAired)
        {
            bool firstSyncFromZero = draft.LastEpisodesSync is null && draft.EpisodesAired == 0;
            if (!firstSyncFromZero && aired > draft.EpisodesAired)
            {
                draft.LastEpisodeAt = requestedAt;
                notifyEpisode = aired;
            }

            draft.EpisodesAired = aired;
            draft.AiredSourcePriority = 2;
        }

        if (airing.TotalEpisodes is { } total && total > draft.TotalEpisodes)
            draft.TotalEpisodes = total;

        if (!string.IsNullOrEmpty(airing.Status) && string.IsNullOrEmpty(draft.StatusDetailed))
        {
            draft.StatusDetailed = airing.Status.ToUpperInvariant() switch
            {
                "RELEASING" => AppConstants.AiringStatus.CurrentlyAiring,
                "NOT_YET_RELEASED" => AppConstants.AiringStatus.NotYetAired,
                "FINISHED" => AppConstants.AiringStatus.FinishedAiring,
                _ => airing.Status
            };
        }

        draft.NextEpisodeAt = nextSlot;
        return notifyEpisode;
    }
}
