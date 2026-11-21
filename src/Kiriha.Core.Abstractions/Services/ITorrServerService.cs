using Kiriha.Core.Domain.Models.TorrServer;

namespace Kiriha.Core.Abstractions.Services;

public interface ITorrServerService : IDisposable
{
    bool IsRunning { get; }
    string ServerUrl { get; }

    Task<bool> EnsureRunningAsync(CancellationToken ct = default);
    Task<TorrServerTorrentStatus?> AddTorrentAsync(string link, string title, CancellationToken ct = default);
    Task<TorrServerPreparation?> PrepareTorrentAsync(string link, string title, CancellationToken ct = default);
    Task<IDisposable> AcquirePlaybackAsync(string streamUrl, string hash, CancellationToken ct = default);
    Task FlushTorrentReleasesAsync(CancellationToken ct = default);
    Task<TorrServerTorrentStatus?> GetTorrentAsync(string hash, CancellationToken ct = default);
    Task<IReadOnlyList<TorrServerFileItem>> WaitForFilesAsync(string hash, TimeSpan timeout, CancellationToken ct = default);
    Task DropTorrentAsync(string hash, CancellationToken ct = default);
    string GetStreamUrl(string hash, int fileId, string fileName);
    void Stop();
}

/// <summary>Owns a torrent while it is being prepared or handed off to a player.</summary>
public sealed record TorrServerPreparation(string ServerUrl, TorrServerTorrentStatus Status, IDisposable Lease) : IDisposable
{
    public void Dispose() => Lease.Dispose();
}
