using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Models;
using Kiriha.Core.Repositories;
using Kiriha.Core.Services;
using Kiriha.Core.Tracking.Integration;

using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public class MediaMatchResult
{
    public bool NegativelyMapped { get; init; }
    public bool Success { get; init; }
    public int? MalId { get; init; }
    public AnimeEntity? MatchedAnime { get; init; }
}

public class MediaMatchingPipeline
{
    private readonly IMappingService _mappingService;
    private readonly IEnumerable<ITrackerService> _trackers;

    public MediaMatchingPipeline(IMappingService mappingService, IEnumerable<ITrackerService> trackers)
    {
        _mappingService = mappingService;
        _trackers = trackers;
    }

    public async Task<MediaMatchResult> RunAsync(ParsedMedia media, List<AnimeEntity> userList)
    {
        if (_mappingService.IsNegativelyMapped(media.OriginalTitle) ||
            _mappingService.IsNegativelyMapped(media.AnimeTitle))
        {
            return new MediaMatchResult { NegativelyMapped = true };
        }

        int? malId = await _mappingService.GetIdFromTitleAsync(media.OriginalTitle, userList);
        if (!malId.HasValue)
        {
            malId = await _mappingService.SearchOnMalAsync(media.OriginalTitle);
        }

        if (malId.HasValue)
        {
            var matched = userList.FirstOrDefault(x => x.Id == malId.Value);
            if (matched == null)
            {
                var activeTracker = _trackers.FirstOrDefault(t => t.IsEnabled);
                if (activeTracker != null)
                {
                    try
                    {
                        var fetched = await activeTracker.GetAnimeDetailsAsync(malId.Value);
                        if (fetched != null)
                        {
                            matched = new AnimeEntity { Id = fetched.Id, Title = fetched.Title, RussianTitle = fetched.RussianTitle, EnglishTitle = fetched.EnglishTitle, TotalEpisodes = fetched.TotalEpisodes, Progress = fetched.Progress, MainPictureUrl = fetched.MainPictureUrl, Status = UserAnimeStatus.None };
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to fetch anime details for ID {AnimeId}", malId.Value);
                    }
                }
            }

            return new MediaMatchResult { Success = true, MatchedAnime = matched, MalId = malId.Value };
        }

        return new MediaMatchResult { Success = false };
    }
}
