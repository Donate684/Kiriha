using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public sealed partial class UserAnimeRepository
{
    private static readonly TimeSpan ProgressCheckpointInterval = TimeSpan.FromSeconds(10);
    private long _lastProgressCheckpointTicks;

    public async Task UpdateProgressAsync(AnimeItem item, int progress, UserAnimeStatus? status = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var shouldUpdateStatus = status.HasValue && status.Value != UserAnimeStatus.None;
        var isManga = item.MediaKind != MediaKind.Anime;

        int affected;

        if (isManga)
        {
            affected = shouldUpdateStatus
                ? await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.ChaptersRead, item.ChaptersRead)
                        .SetProperty(x => x.VolumesRead, item.VolumesRead)
                        .SetProperty(x => x.Status, status!.Value))
                : await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.ChaptersRead, item.ChaptersRead)
                        .SetProperty(x => x.VolumesRead, item.VolumesRead));
        }
        else
        {
            affected = shouldUpdateStatus
                ? await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.Status, status!.Value))
                : await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress));
        }

        if (affected == 0)
        {
            Log.Warning("Attempted to update progress for non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            await UpsertAsync(item);
            return;
        }

        await CheckpointProgressWriteAsync(context, item);
    }

    public async Task UpdateScoreAsync(AnimeItem item, string score)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Score, score));

        if (affected == 0)
        {
            Log.Warning("Attempted to update score for non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            await UpsertAsync(item);
        }
    }

    public async Task UpdateMetadataAsync(AnimeItem item)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var alternativeTitles = new List<string>(item.AlternativeTitles);
        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RussianTitle, item.RussianTitle)
                .SetProperty(x => x.RussianSynopsis, item.RussianSynopsis)
                .SetProperty(x => x.EnglishTitle, item.EnglishTitle)
                .SetProperty(x => x.JapaneseTitle, item.JapaneseTitle)
                .SetProperty(x => x.AlternativeTitles, alternativeTitles));

        if (affected == 0)
        {
            Log.Debug("Skipping metadata-only update for non-user anime {Title} (ID: {Id})", item.Title, item.Id);
        }
    }

    private async Task CheckpointProgressWriteAsync(AppDbContext context, AnimeItem item)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastProgressCheckpointTicks);
        if (lastTicks != 0 && nowTicks - lastTicks < ProgressCheckpointInterval.Ticks)
            return;

        if (Interlocked.CompareExchange(ref _lastProgressCheckpointTicks, nowTicks, lastTicks) != lastTicks)
            return;

        try { await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);"); }
        catch (System.Exception ex) { Log.Warning(ex, "wal_checkpoint(PASSIVE) failed after updating progress for {Title}", item.Title); }
    }
}
