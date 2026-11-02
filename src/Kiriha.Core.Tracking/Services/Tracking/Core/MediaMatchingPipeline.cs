using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public record class MediaMatchSuccess(AnimeEntity? MatchedAnime, int MalId);

public record class MediaNegativelyMapped;

public record class MediaMatchNotFound;

/// <summary>
/// First-class C# 15 discriminated union representing the outcome of media matching.
/// </summary>
public union MediaMatchResult(MediaMatchSuccess, MediaNegativelyMapped, MediaMatchNotFound);

public class MediaMatchingPipeline
{
    private readonly IMappingService _mappingService;
    private readonly IEnumerable<ITrackerService> _trackers;

    public MediaMatchingPipeline(IMappingService mappingService, IEnumerable<ITrackerService> trackers)
    {
        _mappingService = mappingService;
        _trackers = trackers;
    }

    public async Task<MediaMatchResult> RunAsync(ParsedMedia media, IReadOnlyList<AnimeEntity> userList)
    {
        if (_mappingService.IsNegativelyMapped(media.OriginalTitle) ||
            _mappingService.IsNegativelyMapped(media.AnimeTitle))
        {
            return new MediaNegativelyMapped();
        }

        int? malId = await _mappingService.GetIdFromTitleAsync(media.OriginalTitle, userList);
        if (!malId.HasValue)
        {
            malId = await _mappingService.SearchOnMalAsync(media.OriginalTitle);
        }

        if (malId.HasValue)
        {
            AnimeEntity? matched = null;
            for (int i = 0; i < userList.Count; i++)
            {
                if (userList[i].Id == malId.Value)
                {
                    matched = userList[i];
                    break;
                }
            }
            if (matched is null)
            {
                var fetched = await FetchDetailsFromTrackersAsync(malId.Value);
                if (fetched != null)
                {
                    matched = fetched.Clone();
                    matched.Status = UserAnimeStatus.None;
                }
            }
            else if (matched.IsMissingDetails || (matched.MediaKind == MediaKind.Anime && matched.TotalEpisodes <= 0))
            {
                var fetched = await FetchDetailsFromTrackersAsync(malId.Value);
                if (fetched != null)
                {
                    matched.MergeFullDetails(fetched);
                }
            }

            return new MediaMatchSuccess(matched, malId.Value);
        }

        return new MediaMatchNotFound();
    }

    private async Task<AnimeEntity?> FetchDetailsFromTrackersAsync(int animeId)
    {
        foreach (var tracker in _trackers.Where(t => t.IsEnabled).Concat(_trackers.Where(t => !t.IsEnabled)))
        {
            try
            {
                var fetched = await tracker.GetAnimeDetailsAsync(animeId);
                if (fetched != null)
                    return fetched;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch anime details for ID {AnimeId} via {Tracker}", animeId, tracker.TrackerId);
            }
        }
        return null;
    }
}
