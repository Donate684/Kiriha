using Kiriha.Core.Domain.Models.TorrServer;

namespace Kiriha.Core.Abstractions.Services;

public interface ITorrServerService : IDisposable
{
    bool IsRunning { get; }
    string ServerUrl { get; }

    Task<bool> EnsureRunningAsync(CancellationToken ct = default);
    Task<TorrServerTorrentStatus?> AddTorrentAsync(string link, string title, CancellationToken ct = default);
    Task<TorrServerTorrentStatus?> GetTorrentAsync(string hash, CancellationToken ct = default);
    Task<IReadOnlyList<TorrServerFileItem>> WaitForFilesAsync(string hash, TimeSpan timeout, CancellationToken ct = default);
    Task DropTorrentAsync(string hash, CancellationToken ct = default);
    string GetStreamUrl(string hash, int fileId, string fileName);
    void Stop();
}
