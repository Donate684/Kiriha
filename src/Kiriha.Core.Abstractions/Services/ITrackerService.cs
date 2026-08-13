using Kiriha.Core.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Models.Entities;

namespace Kiriha.Core.Services;

public interface ITrackerService
{
    string Name { get; }
    bool IsEnabled { get; }

    Task<List<AnimeEntity>?> GetUserAnimeListAsync(CancellationToken ct = default);
    Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default);
    Task<SyncOutcome> SaveFullListStatusAsync(AnimeEntity item, CancellationToken ct = default);

    Task<List<AnimeEntity>> SearchAnimeAsync(string query, CancellationToken ct = default);
    Task<AnimeEntity?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default);
    Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default);

    Task<List<AnimeEntity>?> GetUserMangaListAsync(CancellationToken ct = default);
    Task<SyncOutcome> UpdateMangaProgressAsync(int mangaId, int chapters, int? volumes = null, UserAnimeStatus? status = null, int? score = null, CancellationToken ct = default);
    Task<List<AnimeEntity>> SearchMangaAsync(string query, CancellationToken ct = default);
    Task<AnimeEntity?> GetMangaDetailsAsync(int mangaId, CancellationToken ct = default);
}
