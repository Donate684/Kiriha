namespace Kiriha.Core.Domain.Models.TorrServer;

public sealed class TorrServerTorrentStatus
{
    public string Hash { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string StatString { get; init; } = string.Empty;
    public int Stat { get; init; }
    public long TorrentSize { get; init; }
    public double DownloadSpeed { get; init; }
    public double UploadSpeed { get; init; }
    public int TotalPeers { get; init; }
    public int ActivePeers { get; init; }
    public int ConnectedSeeders { get; init; }
    public long PreloadSize { get; init; }
    public long PreloadedBytes { get; init; }
    public IReadOnlyList<TorrServerFileItem> Files { get; init; } = [];
}
