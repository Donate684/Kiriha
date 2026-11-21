using Avalonia.Threading;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Serilog;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    private CancellationTokenSource? _sourceStartup;
    private bool _playbackDisposed;
    private int _sourceVersion;
    private string? _pendingPlaybackRequestId;
    private string? _activePlaybackRequestId;

    private void BeginSourcePlayback(string url, IReadOnlyDictionary<string, string>? headers)
    {
        CancelSourceStartup();
        _activePlaybackRequestId = null;
        Interlocked.Increment(ref _sourceVersion);
        if (!_playback.HasPlayer || _playbackDisposed)
            return;

        if (IsTorrentStream && _torrServer is { } server && UsesConfiguredServer(url, server.ServerUrl))
        {
            var request = new CancellationTokenSource();
            _sourceStartup = request;
            _playback.Pause();
            _ = WaitForSourceAsync(server, request, url, headers);
        }
        else
        {
            LoadPreparedSource(url, headers);
        }
    }

    private static bool UsesConfiguredServer(string url, string serverUrl)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var target) &&
            Uri.TryCreate(serverUrl, UriKind.Absolute, out var server) &&
            target.Scheme == server.Scheme && target.Port == server.Port &&
            (target.Host.Equals(server.Host, StringComparison.OrdinalIgnoreCase) || target.IsLoopback && server.IsLoopback);
    }

    private async Task WaitForSourceAsync(ITorrServerService server, CancellationTokenSource request,
        string url, IReadOnlyDictionary<string, string>? headers)
    {
        using (request)
        {
            var ct = request.Token;
            bool ready;
            try
            {
                ready = await server.EnsureRunningAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not prepare the TorrServer playback source");
                ready = false;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_sourceStartup, request) || ct.IsCancellationRequested)
                    return;

                _sourceStartup = null;
                if (ready && UsesConfiguredServer(url, server.ServerUrl))
                {
                    LoadPreparedSource(url, headers);
                    return;
                }

                IsLoading = false;
                _activePlaybackRequestId = _pendingPlaybackRequestId;
                IsPlaying = false;
                IsBuffering = false;
                HasPlaybackError = true;
                PlaybackErrorMessage = _localizer.GetLoc(AppConstants.Player.TorrServerStartFailedLocKey);
                PlaybackStatusMessage = PlaybackErrorMessage;
                _timer?.Stop();
                Kiriha.Infrastructure.Utils.PowerManager.AllowDisplaySleep();
                _statePublisher.Publish();
            });
        }
    }

    private void LoadPreparedSource(string url, IReadOnlyDictionary<string, string>? headers)
    {
        _activePlaybackRequestId = _pendingPlaybackRequestId;
        if (!PlayerAutoPlay)
            _playback.Pause();
        _playback.Load(url, headers);
        if (PlayerAutoPlay)
            _playback.Play();
        _statePublisher.Publish();
    }

    private void CancelSourceStartup()
    {
        var request = _sourceStartup;
        _sourceStartup = null;
        request?.Cancel();
    }
}
