namespace Kiriha.Core.Abstractions.Services;

/// <summary>
/// Unified service coordinating the complete anime/manga refresh cycle:
/// synchronizing titles, progress and status with cloud trackers,
/// followed by fetching current episode airing schedules.
/// </summary>
public interface IAnimeRefreshService
{
    bool IsRefreshing { get; }

    System.Threading.Tasks.Task<bool> RefreshAnimeListAsync(
        System.IProgress<string>? progress = null,
        System.Threading.CancellationToken ct = default,
        bool isMigration = false);

    System.Threading.Tasks.Task<bool> RefreshMangaListAsync(
        System.IProgress<string>? progress = null,
        System.Threading.CancellationToken ct = default,
        bool isMigration = false);
}
