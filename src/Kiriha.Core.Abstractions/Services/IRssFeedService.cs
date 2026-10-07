using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IRssFeedService
{
    Task<List<TorrentEntity>> SearchTorrentsAsync(string query);
}
