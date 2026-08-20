namespace Kiriha.Core.Abstractions.Services;

public interface IAnimeSyncOrchestrator
{
    bool IsSyncing { get; }
    System.Threading.Tasks.Task<bool> SyncWithTrackersAsync(System.IProgress<string>? status = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<bool> SyncMangaWithTrackersAsync(System.IProgress<string>? status = null, System.Threading.CancellationToken ct = default);
}
