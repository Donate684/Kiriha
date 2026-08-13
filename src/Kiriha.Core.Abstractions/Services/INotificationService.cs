using Kiriha.Models.Entities;
namespace Kiriha.Core.Services;

public interface INotificationService
{
    void NotifyScrobbleSkipped(Kiriha.Models.Entities.AnimeEntity anime, int episode);
    void NotifyNewEpisode(Kiriha.Models.Entities.AnimeEntity anime, int episode);
}
