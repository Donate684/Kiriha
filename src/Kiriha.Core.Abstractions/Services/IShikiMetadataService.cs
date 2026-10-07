using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IShikiMetadataService
{
    Task<ShikiMetadata?> GetOrFetchMetadataAsync(
        int animeId,
        TimeSpan? maxAge = null,
        Func<ShikiMetadata, Task>? onFetched = null,
        MediaKind mediaKind = MediaKind.Anime);

    Task<string?> FetchPosterFromGraphQlAsync(int animeId, MediaKind mediaKind, CancellationToken ct);

    Task LocalizeItemsAsync(
        IEnumerable<AnimeEntity> items,
        Action<int>? onProgress = null,
        CancellationToken ct = default);

    Task EnsureLocalizedAsync(AnimeEntity item, CancellationToken ct = default);

    Task<string?> ResolveRussianQueryAsync(string query, CancellationToken ct = default);
}
