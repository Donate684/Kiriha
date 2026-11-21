using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Utils;
using Serilog;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    [RelayCommand]
    public void DownloadMagnet(TorrentEntity torrent)
    {
        if (torrent is null || string.IsNullOrEmpty(torrent.MagnetLink)) return;
        UIUtils.OpenUrl(torrent.MagnetLink);
    }

    [RelayCommand]
    public void DownloadTorrentFile(TorrentEntity torrent)
    {
        if (torrent is null || string.IsNullOrEmpty(torrent.DownloadLink)) return;
        UIUtils.OpenUrl(torrent.DownloadLink);
    }

    [RelayCommand]
    public void DismissStreamingError() => StreamingErrorMessage = null;

    [RelayCommand]
    public async Task PlayTorrentAsync(TorrentEntity torrent)
    {
        if (torrent is null) return;
        var link = !string.IsNullOrWhiteSpace(torrent.MagnetLink) ? torrent.MagnetLink : torrent.DownloadLink;
        if (string.IsNullOrWhiteSpace(link)) return;

        StreamingErrorMessage = null;
        CancellationToken launchToken = default;

        try
        {
            if (torrent.IsBatch)
            {
                var previous = _modalCts;
                _modalCts = new CancellationTokenSource();
                var ct = _modalCts.Token;
                if (previous != null)
                {
                    await previous.CancelAsync();
                    previous.Dispose();
                }
                ct.ThrowIfCancellationRequested();

                ActiveStreamingTorrent = torrent;
                EpisodeFiles.Clear();
                IsEpisodeSelectorVisible = true;
                IsEpisodeSelectorLoading = true;
                EpisodeSelectorError = null;
                EpisodeSelectorCountText = string.Empty;

                var isReady = await _torrServer.EnsureRunningAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (!isReady)
                {
                    EpisodeSelectorError = _localizer.GetLoc(AppConstants.Player.TorrServerStartFailedLocKey);
                    IsEpisodeSelectorLoading = false;
                    return;
                }

                var status = await _torrServer.AddTorrentAsync(link, torrent.Title, ct);
                ct.ThrowIfCancellationRequested();
                if (status is null)
                {
                    EpisodeSelectorError = _localizer.GetLoc("torrents.streaming.add_failed");
                    IsEpisodeSelectorLoading = false;
                    return;
                }

                _currentStreamingHash = status.Hash;

                var files = await _torrServer.WaitForFilesAsync(status.Hash, TimeSpan.FromSeconds(35), ct);
                ct.ThrowIfCancellationRequested();
                var videoFiles = files.Where(f => f.IsVideo).ToList();

                if (videoFiles.Count == 0)
                {
                    EpisodeSelectorError = _localizer.GetLoc("torrents.streaming.no_video");
                    IsEpisodeSelectorLoading = false;
                    if (!string.IsNullOrEmpty(_currentStreamingHash))
                    {
                        var hash = _currentStreamingHash;
                        _currentStreamingHash = null;
                        _ = Task.Run(async () =>
                        {
                            try { await _torrServer.DropTorrentAsync(hash, CancellationToken.None).ConfigureAwait(false); }
                            catch (Exception ex) { Log.Warning(ex, "Failed to drop empty batch torrent {Hash}", hash); }
                        }, CancellationToken.None);
                    }
                    return;
                }

                foreach (var f in videoFiles)
                {
                    EpisodeFiles.Add(f);
                }
                EpisodeSelectorCountText = _localizer.GetLoc(EpisodesCountLocKey, EpisodeFiles.Count);
                IsEpisodeSelectorLoading = false;
            }
            else
            {
                var ct = BeginPlayerLaunch();
                launchToken = ct;

                IsPlayerLaunching = true;
                PlayerLaunchingTitle = torrent.Title;

                var isReady = await _torrServer.EnsureRunningAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (!isReady)
                {
                    Log.Warning("TorrentsViewModel: TorrServer is not ready");
                    IsPlayerLaunching = false;
                    StreamingErrorMessage = _localizer.GetLoc(AppConstants.Player.TorrServerStartFailedLocKey);
                    return;
                }

                var status = await _torrServer.AddTorrentAsync(link, torrent.Title, ct);
                ct.ThrowIfCancellationRequested();
                if (status is null)
                {
                    Log.Warning("TorrentsViewModel: Failed to add torrent to TorrServer");
                    IsPlayerLaunching = false;
                    StreamingErrorMessage = _localizer.GetLoc("torrents.streaming.add_failed");
                    return;
                }

                _currentStreamingHash = status.Hash;

                var files = await _torrServer.WaitForFilesAsync(status.Hash, TimeSpan.FromSeconds(12), ct);
                ct.ThrowIfCancellationRequested();
                var videoFiles = files.Where(f => f.IsVideo).ToList();
                if (files.Count > 0 && videoFiles.Count == 0)
                {
                    Log.Warning("TorrentsViewModel: Torrent {Hash} resolved metadata ({Count} files) with no video files", status.Hash, files.Count);
                    await _torrServer.DropTorrentAsync(status.Hash, ct);
                    ct.ThrowIfCancellationRequested();
                    _currentStreamingHash = null;
                    IsPlayerLaunching = false;
                    StreamingErrorMessage = _localizer.GetLoc("torrents.streaming.no_video");
                    return;
                }

                var targetFile = videoFiles.OrderByDescending(f => f.Length).FirstOrDefault();
                var fileId = targetFile?.Id ?? -1;
                var fileName = targetFile?.FileName ?? (torrent.Title + ".mkv");

                var streamHash = status.Hash;
                _currentStreamingHash = null;
                _launchedStreamingHash = streamHash;

                var streamUrl = _torrServer.GetStreamUrl(streamHash, fileId, fileName);
                await OpenStreamAsync(streamUrl, torrent, fileName, streamHash, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TorrentsViewModel: Streaming error for '{Title}'", torrent.Title);
            if (torrent.IsBatch && IsEpisodeSelectorVisible)
            {
                EpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
                IsEpisodeSelectorLoading = false;
            }
            else if (!torrent.IsBatch)
            {
                StreamingErrorMessage = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
            }
        }
        finally
        {
            if (!torrent.IsBatch)
                FinishPlayerLaunch(launchToken);
        }
    }

    [RelayCommand]
    public async Task SelectEpisodeFile(Kiriha.Core.Domain.Models.TorrServer.TorrServerFileItem file)
    {
        if (file is null || string.IsNullOrEmpty(_currentStreamingHash) || ActiveStreamingTorrent is null)
            return;

        var torrent = ActiveStreamingTorrent;
        var streamHash = _currentStreamingHash;
        _currentStreamingHash = null;
        _launchedStreamingHash = streamHash;

        var streamUrl = _torrServer.GetStreamUrl(streamHash, file.Id, file.FileName);
        CloseEpisodeSelector(dropTorrent: false);

        var ct = BeginPlayerLaunch();

        IsPlayerLaunching = true;
        PlayerLaunchingTitle = $"{torrent.Title} - {file.FileName}";

        try
        {
            await OpenStreamAsync(streamUrl, torrent, file.FileName, streamHash, ct);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        finally
        {
            FinishPlayerLaunch(ct);
        }
    }

    private CancellationToken BeginPlayerLaunch()
    {
        var previous = _launchCts;
        _launchCts = new CancellationTokenSource();
        previous?.Cancel();
        previous?.Dispose();
        return _launchCts.Token;
    }

    private void FinishPlayerLaunch(CancellationToken ct) => Dispatcher.UIThread.Post(() =>
    {
        if (!ct.IsCancellationRequested && _launchCts?.Token == ct)
            IsPlayerLaunching = false;
    });

    [RelayCommand]
    public void CloseEpisodeSelector() => CloseEpisodeSelector(dropTorrent: true);

    public void CloseEpisodeSelector(bool dropTorrent)
    {
        _modalCts?.Cancel();
        IsEpisodeSelectorVisible = false;
        IsEpisodeSelectorLoading = false;
        EpisodeSelectorError = null;
        EpisodeFiles.Clear();
        EpisodeSelectorCountText = string.Empty;
        ActiveStreamingTorrent = null;

        if (dropTorrent && !string.IsNullOrEmpty(_currentStreamingHash))
        {
            var hash = _currentStreamingHash;
            _currentStreamingHash = null;
            _ = Task.Run(async () =>
            {
                try { await _torrServer.DropTorrentAsync(hash, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "Failed to drop cancelled batch torrent {Hash}", hash); }
            }, CancellationToken.None);
        }
    }

    [RelayCommand]
    public void CancelStreaming()
    {
        CloseEpisodeSelector();
    }

    [RelayCommand]
    public void CancelPlayerLaunching()
    {
        _launchCts?.Cancel();
        IsPlayerLaunching = false;
        var hash = !string.IsNullOrEmpty(_currentStreamingHash) ? _currentStreamingHash : _launchedStreamingHash;
        _currentStreamingHash = null;
        _launchedStreamingHash = null;
        if (!string.IsNullOrEmpty(hash))
        {
            _ = Task.Run(async () =>
            {
                try { await _torrServer.DropTorrentAsync(hash, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "Failed to drop cancelled launch torrent {Hash}", hash); }
            }, CancellationToken.None);
        }
    }

    private async Task OpenStreamAsync(string url, TorrentEntity torrent, string fileName, string torrentHash, CancellationToken ct)
    {
        var opened = await LaunchAndWaitForPlayerAsync(url, torrent, fileName, torrentHash, ct);
        ct.ThrowIfCancellationRequested();
        if (string.Equals(_launchedStreamingHash, torrentHash, StringComparison.OrdinalIgnoreCase))
            _launchedStreamingHash = null;

        if (!opened)
        {
            StreamingErrorMessage = _localizer.GetLoc(AppConstants.Player.OpenFailedLocKey);
            await _torrServer.DropTorrentAsync(torrentHash, ct);
        }
    }

    private async Task<bool> LaunchAndWaitForPlayerAsync(
        string url, TorrentEntity torrent, string fileName, string torrentHash, CancellationToken ct)
    {
        const int noIpcDelayMs = 2500;
        const int playerWakeupTimeoutSeconds = 25;
        var requestId = Guid.NewGuid().ToString("N");
        if (_internalPlayerServer == null)
        {
            if (!await LaunchStreamAsync(url, torrent, fileName, torrentHash, requestId, ct))
                return false;
            await Task.Delay(noIpcDelayMs, ct);
            return true;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPlayerStateChanged(object? sender, InternalPlayerState state)
        {
            if (!state.IsClosed && state.PlaybackRequestId == requestId)
                tcs.TrySetResult(!state.HasPlaybackError);
        }

        _internalPlayerServer.PlayerStateChanged += OnPlayerStateChanged;
        try
        {
            return await LaunchStreamAsync(url, torrent, fileName, torrentHash, requestId, ct) &&
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(playerWakeupTimeoutSeconds), ct);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            _internalPlayerServer.PlayerStateChanged -= OnPlayerStateChanged;
        }
    }

    private Task<bool> LaunchStreamAsync(
        string streamUrl, TorrentEntity torrent, string fileName, string torrentHash, string requestId, CancellationToken ct)
    {
        var originalTitle = !string.IsNullOrWhiteSpace(fileName) ? fileName : torrent.Title;
        string? episode = null;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var parsedFromFile = Kiriha.Core.Domain.Utils.Parsing.AnimeParseCache.Parse(System.IO.Path.GetFileNameWithoutExtension(fileName)).Episode;
            if (int.TryParse(parsedFromFile, out _))
            {
                episode = parsedFromFile;
            }
        }

        if (string.IsNullOrWhiteSpace(episode) && !string.IsNullOrWhiteSpace(torrent.Episode) && !torrent.IsBatch)
        {
            episode = torrent.Episode;
        }

        var effectiveSelected = IsSelectedAnimeCompatibleWithTorrent(SelectedAnime, torrent, originalTitle)
            ? SelectedAnime
            : null;
        var titleRu = effectiveSelected?.RussianTitle ?? (!string.IsNullOrWhiteSpace(torrent.AnimeTitle) ? torrent.AnimeTitle : string.Empty);
        var titleEn = effectiveSelected?.Title ?? torrent.Title;

        if (_internalPlayerServer != null)
        {
            var initialMeta = new PlayerMediaMetadata(
                originalTitle,
                titleRu,
                titleEn,
                episode ?? string.Empty,
                effectiveSelected?.Id,
                titleEn,
                torrentHash);

            _internalPlayerServer.SendMetadataAsync(initialMeta, CancellationToken.None).SafeFireAndForget("Torrents.SendMetadata");
        }

        return PlayerProcessHelper.LaunchPlayerAsync(
            videoUrl: streamUrl,
            titleRu: titleRu,
            titleEn: titleEn,
            episode: episode,
            animeId: effectiveSelected?.Id,
            torrentHash: torrentHash,
            originalTitle: originalTitle,
            ct: ct, requestId: requestId);
    }

    private bool IsSelectedAnimeCompatibleWithTorrent(AnimeEntity? selected, TorrentEntity torrent, string originalTitle) =>
        Kiriha.Core.Domain.Utils.Parsing.AnimeLibraryMatcher.IsReleaseCompatibleWithAnime(
            selected,
            primaryReleaseTitle: originalTitle,
            fallbackReleaseTitle: torrent.Title,
            fallbackEpisode: torrent.Episode,
            enforceSeasonAndEpisode: !UseCustomQuery,
            activeSearchQuery: HasSearchQuery ? SearchQuery : null);
}
