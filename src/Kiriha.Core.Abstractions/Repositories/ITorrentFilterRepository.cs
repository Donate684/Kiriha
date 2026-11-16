using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Repositories;

public interface ITorrentFilterRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    IReadOnlySet<int> GetHiddenAnimeIds();
    Task SetAnimeHiddenAsync(int animeId, bool hidden, CancellationToken ct = default);
    AppSettings.TorrentFilterSet? TryGetCachedFilter(int animeId);
    Task SaveFilterAsync(int animeId, AppSettings.TorrentFilterSet filter, CancellationToken ct = default);
}
