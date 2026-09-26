using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Repositories;

public interface ITorrentFilterRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    IReadOnlySet<int> GetHiddenAnimeIds();
    bool IsAnimeHidden(int animeId);
    Task SetAnimeHiddenAsync(int animeId, bool hidden, CancellationToken ct = default);
    AppSettings.TorrentFilterSet? TryGetCachedFilter(int animeId);
    Task<AppSettings.TorrentFilterSet?> GetFilterAsync(int animeId, CancellationToken ct = default);
    Task SaveFilterAsync(int animeId, AppSettings.TorrentFilterSet filter, CancellationToken ct = default);
}
