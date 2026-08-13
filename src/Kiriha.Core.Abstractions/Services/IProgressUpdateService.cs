using System.Threading.Tasks;
using Kiriha.Models.Entities;

namespace Kiriha.Core.Services;

public interface IProgressUpdateService
{
    Task<bool> UpdateProgressAsync(AnimeEntity anime, int nextProgress, UserAnimeStatus? nextStatus = null);
    Task RemoveAnimeAsync(int animeId);
}
