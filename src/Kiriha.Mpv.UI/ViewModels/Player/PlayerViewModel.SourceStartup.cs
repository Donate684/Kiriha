using Avalonia.Threading;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.TorrServer;
using Serilog;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    private CancellationTokenSource? _sourceStartup;
    private readonly List<Task> _sourcePreparations = [];
    private bool _playbackDisposed;
    private int _sourceVersion;
    private long _nativeSourceId;
    private string _pendingPlaybackRequestId;
    private string? _activePlaybackRequestId;
    private string? _playbackSourceKey;
    private PlaybackOpeningState _openingState;
    private bool _waitingForRenderContext;
    private readonly Dictionary<TorrServerStreamSource, IDisposable> _torrentLeases = [];
    private TorrServerStreamSource? _selectedTorrent;

    private static string NewPlaybackRequestId(string? requestId) =>
        string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;

    public PlaybackOpeningState GetOpeningState(string requestId) =>
        !_playbackDisposed && string.Equals(requestId, _pendingPlaybackRequestId, StringComparison.Ordinal)
            ? _openingState
            : PlaybackOpeningState.Superseded;

    public void ReportPlaybackInitializationFailure()
    {
        _activePlaybackRequestId = _pendingPlaybackRequestId;
        FailPlayback(0, 0, errorMessage: null);
    }

    public void WaitForRenderContext() => _waitingForRenderContext = true;

    private void BeginSourcePlayback(string url, IReadOnlyDictionary<string, string>? headers)
    {
        CancelSourceStartup();
        _activePlaybackRequestId = null;
        _nativeSourceId = 0;
        _highestObservedDuration = 0;
        _selectedTorrent = null;
        _playbackSourceKey = ParsedMedia.CreatePlaybackSourceKey(url);
        Interlocked.Increment(ref _sourceVersion);
        if (!_playback.HasPlayer || _playbackDisposed || _waitingForRenderContext)
            return;

        var torrent = TorrServerStreamSource.Parse(url);
        var server = _torrServer;
        var ensureServer = IsTorrentStream && server != null && UsesConfiguredServer(url, server.ServerUrl);
        if (server != null && (ensureServer || torrent != null))
        {
            var request = new CancellationTokenSource();
            _sourceStartup = request;
            _playback.Pause();
            var needsLease = torrent is { } key && !_torrentLeases.ContainsKey(key);
            _sourcePreparations.RemoveAll(task => task.IsCompletedSuccessfully);
            _sourcePreparations.Add(WaitForSourceAsync(server, request, url, headers, torrent, ensureServer, needsLease));
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
        string url, IReadOnlyDictionary<string, string>? headers, TorrServerStreamSource? torrent,
        bool ensureServer, bool needsLease)
    {
        using (request)
        {
            var ct = request.Token;
            IDisposable? lease = null;
            bool ready;
            try
            {
                ready = !ensureServer || await server.EnsureRunningAsync(ct).ConfigureAwait(false);
                if (ready && needsLease && torrent is { } key)
                    lease = await server.AcquirePlaybackAsync(url, key.Hash, ct).ConfigureAwait(false);
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

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_sourceStartup, request) || ct.IsCancellationRequested)
                        return;

                    _sourceStartup = null;
                    if (ready && (!ensureServer || UsesConfiguredServer(url, server.ServerUrl)) &&
                        (!needsLease || lease != null))
                    {
                        if (torrent is { } source && lease != null)
                        {
                            _torrentLeases.Add(source, lease);
                            lease = null;
                        }
                        _selectedTorrent = torrent;
                        LoadPreparedSource(url, headers);
                        return;
                    }

                    IsLoading = false;
                    _openingState = PlaybackOpeningState.Failed;
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
            finally
            {
                lease?.Dispose();
            }
        }
    }

    private void ReleaseReplacedTorrents(TorrServerStreamSource? keep)
    {
        foreach (var key in _torrentLeases.Keys.Where(key => key != keep).ToArray())
        {
            var lease = _torrentLeases[key];
            _torrentLeases.Remove(key);
            lease.Dispose();
        }
    }

    private void LoadPreparedSource(string url, IReadOnlyDictionary<string, string>? headers)
    {
        _activePlaybackRequestId = _pendingPlaybackRequestId;
        _nativeSourceId = _playback.LoadSource(url, headers, PlayerAutoPlay);
        _statePublisher.Publish();
    }

    private bool IsCurrentNativeSource(long sourceId) =>
        sourceId != 0 && sourceId == _nativeSourceId && !_playbackDisposed && _sourceStartup == null;

    private void CancelSourceStartup()
    {
        var request = _sourceStartup;
        _sourceStartup = null;
        request?.Cancel();
    }
}
