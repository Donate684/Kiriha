using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface ITrackingService : IDisposable
{
    ParsedMedia? CurrentMedia { get; }
    AnimeEntity? MatchedAnime { get; }
    bool IsManuallyMapped();
    Task ManualMapAsync(int animeId);
    Task RemoveManualMappingAsync();
    Task AddNegativeMappingAsync();
    void NotifyCurrentMediaMetadataUpdated(AnimeEntity anime);
}
