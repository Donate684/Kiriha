using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IProgressUpdateService
{
    Task<bool> UpdateProgressAsync(AnimeEntity anime, int nextProgress, UserAnimeStatus? nextStatus = null);
    Task RemoveAnimeAsync(int animeId);
    Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeEntity item, int nextProgress);
    Task SmartDecrementProgressAsync(AnimeEntity item);
    Task SetScoreAsync(AnimeEntity item, int score);
    Task ConfirmRewatchAsync(AnimeEntity item, int episode = 1);
}
