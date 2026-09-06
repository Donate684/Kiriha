using Kiriha.Core.Domain.Models.Entities;
namespace Kiriha.Core.Abstractions.Services;

public interface INotificationService
{
    void NotifyScrobbleSkipped(AnimeEntity anime, int episode);
    void NotifyNewEpisode(AnimeEntity anime, int episode);
    void NotifyAnimeCompleted(AnimeEntity anime);
}
