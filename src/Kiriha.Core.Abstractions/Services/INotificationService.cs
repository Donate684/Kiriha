using Kiriha.Core.Abstractions.Models.Entities;
namespace Kiriha.Core.Services;

public interface INotificationService
{
    void NotifyScrobbleSkipped(AnimeEntity anime, int episode);
    void NotifyNewEpisode(AnimeEntity anime, int episode);
}
