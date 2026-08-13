using Kiriha.Core.Domain.Models.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Core.Abstractions.Services;

public interface ISyncManager
{
    Task EnqueueUpdateAsync(int animeId, int progress, UserAnimeStatus? status = null, int? score = null);
    Task EnqueueRemoveAsync(int animeId);
    Task EnqueueFullUpdateAsync(AnimeEntity item);
}
