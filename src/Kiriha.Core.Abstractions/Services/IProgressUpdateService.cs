using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IProgressUpdateService
{
    Task<bool> UpdateProgressAsync(AnimeEntity anime, int nextProgress, UserAnimeStatus? nextStatus = null);
    Task RemoveAnimeAsync(int animeId);
}
