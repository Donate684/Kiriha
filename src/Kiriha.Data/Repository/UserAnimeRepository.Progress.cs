using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public sealed partial class UserAnimeRepository
{
    public async Task UpdateProgressAsync(AnimeEntity item, int progress, UserAnimeStatus? status = null)
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
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted)
                        .SetProperty(x => x.Status, status!.Value))
                : await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.ChaptersRead, item.ChaptersRead)
                        .SetProperty(x => x.VolumesRead, item.VolumesRead)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted));
        }
        else
        {
            affected = shouldUpdateStatus
                ? await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted)
                        .SetProperty(x => x.Status, status!.Value))
                : await context.UserAnime
                    .Where(x => x.Id == item.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Progress, progress)
                        .SetProperty(x => x.IsRewatching, item.IsRewatching)
                        .SetProperty(x => x.RewatchCount, item.RewatchCount)
                        .SetProperty(x => x.DateStarted, item.DateStarted)
                        .SetProperty(x => x.DateCompleted, item.DateCompleted));
        }

        if (affected == 0)
        {
            Log.Warning("Attempted to update progress for non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            await UpsertAsync(item);
            return;
        }
    }

    public async Task UpdateScoreAsync(AnimeEntity item, string score)
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

    public async Task UpdateMetadataAsync(AnimeEntity item)
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
}
