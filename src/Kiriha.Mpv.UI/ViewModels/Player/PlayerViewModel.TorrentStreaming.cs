using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.TorrServer;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private bool _isTorrentStream;
    [ObservableProperty] private string _torrentHash = string.Empty;
    [ObservableProperty] private string _torrentDownloadSpeedText = string.Empty;
    [ObservableProperty] private string _torrentUploadSpeedText = string.Empty;
    [ObservableProperty] private string _torrentPeersText = string.Empty;
    [ObservableProperty] private string _torrentPeersBadgeText = string.Empty;
    [ObservableProperty] private string _torrentBufferText = string.Empty;
    [ObservableProperty] private double _torrentBufferPercent;
    [ObservableProperty] private bool _isTorrentBufferIndeterminate = true;
    [ObservableProperty] private string _torrentStatusText = string.Empty;
    [ObservableProperty] private string _torrentDetailsTooltip = string.Empty;
    [ObservableProperty] private bool _isBuffering;
    [ObservableProperty] private bool _isRebuffering;
    [ObservableProperty] private string _bufferingStatusText = string.Empty;

    public void UpdateRebufferingState()
    {
        IsRebuffering = IsBuffering && !IsLoading;
    }

    private CancellationTokenSource? _torrentMonitorCts;

    public bool MatchesTorrentHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return false;

        return string.Equals(TorrentHash, hash, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrEmpty(VideoUrl) && VideoUrl.Contains(hash, StringComparison.OrdinalIgnoreCase));
    }

    private void InitializeTorrentStream(string videoUrl, PlayerMediaMetadata? metadata)
    {
        _torrentMonitorCts?.Cancel();
        _torrentMonitorCts?.Dispose();
        _torrentMonitorCts = null;

        string hash = metadata?.TorrentHash ?? string.Empty;
        if (string.IsNullOrEmpty(hash) && !string.IsNullOrWhiteSpace(videoUrl))
        {
            var match = System.Text.RegularExpressions.Regex.Match(videoUrl, AppConstants.Torrents.TorrentHashLinkRegex);
            if (match.Success)
            {
                hash = match.Groups[1].Value;
            }
        }

        var isTorrServer = !string.IsNullOrEmpty(hash) ||
                           (!string.IsNullOrWhiteSpace(videoUrl) &&
                            (videoUrl.Contains(AppConstants.Api.TorrServer.DefaultHost, StringComparison.OrdinalIgnoreCase) ||
                             videoUrl.Contains("127.0.0.1:8090", StringComparison.OrdinalIgnoreCase)));

        IsTorrentStream = isTorrServer;
        TorrentHash = hash;

        if (IsTorrentStream && !string.IsNullOrEmpty(TorrentHash) && _torrServer != null)
        {
            TorrentStatusText = _localizer.GetLoc("player.streaming.connecting_peers");
            StartTorrentMonitor(TorrentHash);
        }
    }

    private void StartTorrentMonitor(string hash)
    {
        _torrentMonitorCts = new CancellationTokenSource();
        var ct = _torrentMonitorCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_torrServer == null)
                        break;

                    var status = await _torrServer.GetTorrentAsync(hash, ct).ConfigureAwait(false);
                    if (status != null)
                    {
                        Dispatcher.UIThread.Post(() => ApplyTorrentStatus(status));
                    }

                    var delayMs = (IsLoading || IsBuffering) ? 600 : 1200;
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient network errors during playback monitoring
                    try
                    {
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }, ct);
    }

    private void ApplyTorrentStatus(TorrServerTorrentStatus status)
    {
        TorrentDownloadSpeedText = FormatSpeed(status.DownloadSpeed);
        TorrentUploadSpeedText = FormatSpeed(status.UploadSpeed);
        TorrentPeersText = _localizer.GetLoc("player.streaming.peers_format", status.ActivePeers, status.ConnectedSeeders);
        TorrentPeersBadgeText = $"{status.ActivePeers}/{status.ConnectedSeeders}";

        if (status.PreloadSize > 0)
        {
            var pct = Math.Clamp((double)status.PreloadedBytes / status.PreloadSize * 100.0, 0, 100);
            TorrentBufferPercent = pct;
            IsTorrentBufferIndeterminate = false;
            TorrentBufferText = _localizer.GetLoc(
                "player.streaming.cache_format",
                FormatBytes(status.PreloadedBytes),
                FormatBytes(status.PreloadSize),
                pct.ToString("0"));
        }
        else
        {
            TorrentBufferPercent = 0;
            IsTorrentBufferIndeterminate = true;
            TorrentBufferText = string.Empty;
        }

        if (status.Stat == 1)
        {
            TorrentStatusText = _localizer.GetLoc("player.streaming.preloading_buffer");
        }
        else if (status.Stat == 2)
        {
            TorrentStatusText = _localizer.GetLoc("player.streaming.active_stream");
        }
        else if (status.ActivePeers == 0)
        {
            TorrentStatusText = _localizer.GetLoc("player.streaming.connecting_peers");
        }
        else
        {
            TorrentStatusText = _localizer.GetLoc("player.streaming.buffering");
        }

        TorrentDetailsTooltip = _localizer.GetLoc(
            "player.streaming.pill_tooltip",
            TorrentDownloadSpeedText,
            TorrentUploadSpeedText,
            status.ActivePeers,
            status.ConnectedSeeders,
            FormatBytes(status.PreloadedBytes),
            FormatBytes(status.PreloadSize > 0 ? status.PreloadSize : status.TorrentSize));

        UpdateRebufferingState();
        if (IsLoading)
        {
            var peersSuffix = !string.IsNullOrEmpty(TorrentPeersBadgeText) ? $" • {TorrentPeersBadgeText}" : string.Empty;
            PlaybackStatusMessage = $"{TorrentStatusText} • {TorrentDownloadSpeedText}{peersSuffix}";
        }

        if (IsBuffering)
        {
            BufferingStatusText = $"{_localizer.GetLoc("player.streaming.buffering")} • {TorrentDownloadSpeedText} • {TorrentPeersBadgeText}";
        }
    }

    private void OnPlayerBufferingChanged(bool isBuffering)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsBuffering = isBuffering;
            UpdateRebufferingState();
            if (isBuffering && IsTorrentStream)
            {
                BufferingStatusText = $"{_localizer.GetLoc("player.streaming.buffering")} • {TorrentDownloadSpeedText} • {TorrentPeersBadgeText}";
            }
        });
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0)
            return "0 KB/s";

        if (bytesPerSec < 1024 * 1024)
            return $"{bytesPerSec / 1024:0.#} KB/s";

        return $"{bytesPerSec / (1024 * 1024):0.1} MB/s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 MB";

        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):0.#} MB";

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.2} GB";
    }

    private void DisposeTorrentMonitor()
    {
        _torrentMonitorCts?.Cancel();
        _torrentMonitorCts?.Dispose();
        _torrentMonitorCts = null;
    }
}
