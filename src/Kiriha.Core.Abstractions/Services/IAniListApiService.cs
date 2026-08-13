using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Models;

namespace Kiriha.Core.Services;

public interface IAniListApiService
{
    Task<AniListAiringInfo?> GetNextAiringAsync(int malId, bool force = false, CancellationToken ct = default);
}
