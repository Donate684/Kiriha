using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
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
                _modalCts?.Cancel();
                _modalCts?.Dispose();
                _modalCts = new CancellationTokenSource();
                var ct = _modalCts.Token;

                ActiveStreamingTorrent = torrent;
                EpisodeFiles.Clear();
                IsEpisodeSelectorVisible = true;
                IsEpisodeSelectorLoading = true;
                EpisodeSelectorError = null;
                EpisodeSelectorCountText = string.Empty;

                var isReady = await _torrServer.EnsureRunningAsync(ct);
                if (!isReady)
                {
                    EpisodeSelectorError = _localizer.GetLoc("torrents.streaming.start_failed");
                    IsEpisodeSelectorLoading = false;
                    return;
                }

                var status = await _torrServer.AddTorrentAsync(link, torrent.Title, ct);
                if (status is null)
                {
                    EpisodeSelectorError = _localizer.GetLoc("torrents.streaming.add_failed");
                    IsEpisodeSelectorLoading = false;
                    return;
                }

                _currentStreamingHash = status.Hash;

                var files = await _torrServer.WaitForFilesAsync(status.Hash, TimeSpan.FromSeconds(35), ct);
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
                            try { await _torrServer.DropTorrentAsync(hash).ConfigureAwait(false); }
                            catch (Exception ex) { Log.Warning(ex, "Failed to drop empty batch torrent {Hash}", hash); }
                        });
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
                _launchCts?.Cancel();
                _launchCts?.Dispose();
                _launchCts = new CancellationTokenSource();
                var ct = _launchCts.Token;
                launchToken = ct;

                IsPlayerLaunching = true;
                PlayerLaunchingTitle = torrent.Title;

                var isReady = await _torrServer.EnsureRunningAsync(ct);
                if (!isReady)
                {
                    Log.Warning("TorrentsViewModel: TorrServer is not ready");
                    IsPlayerLaunching = false;
                    StreamingErrorMessage = _localizer.GetLoc("torrents.streaming.start_failed");
                    return;
                }

                var status = await _torrServer.AddTorrentAsync(link, torrent.Title, ct);
                if (status is null)
                {
                    Log.Warning("TorrentsViewModel: Failed to add torrent to TorrServer");
                    IsPlayerLaunching = false;
                    StreamingErrorMessage = _localizer.GetLoc("torrents.streaming.add_failed");
                    return;
                }

                _currentStreamingHash = status.Hash;

                var files = await _torrServer.WaitForFilesAsync(status.Hash, TimeSpan.FromSeconds(12), ct);
                var videoFiles = files.Where(f => f.IsVideo).ToList();
                if (files.Count > 0 && videoFiles.Count == 0)
                {
                    Log.Warning("TorrentsViewModel: Torrent {Hash} resolved metadata ({Count} files) with no video files", status.Hash, files.Count);
                    await _torrServer.DropTorrentAsync(status.Hash, ct).ConfigureAwait(false);
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
                LaunchStream(streamUrl, torrent, fileName, streamHash);

                await WaitForPlayerWakeupAsync(streamHash, torrent.Title, ct).ConfigureAwait(false);
                if (string.Equals(_launchedStreamingHash, streamHash, StringComparison.OrdinalIgnoreCase))
                {
                    _launchedStreamingHash = null;
                }
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
            if (!torrent.IsBatch && !launchToken.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsPlayerLaunching = false);
            }
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

        _launchCts?.Cancel();
        _launchCts?.Dispose();
        _launchCts = new CancellationTokenSource();
        var ct = _launchCts.Token;

        IsPlayerLaunching = true;
        PlayerLaunchingTitle = $"{torrent.Title} - {file.FileName}";

        try
        {
            LaunchStream(streamUrl, torrent, file.FileName, streamHash);
            await WaitForPlayerWakeupAsync(streamHash, torrent.Title, ct).ConfigureAwait(false);
            if (string.Equals(_launchedStreamingHash, streamHash, StringComparison.OrdinalIgnoreCase))
            {
                _launchedStreamingHash = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsPlayerLaunching = false);
            }
        }
    }

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
                try { await _torrServer.DropTorrentAsync(hash).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "Failed to drop cancelled batch torrent {Hash}", hash); }
            });
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
                try { await _torrServer.DropTorrentAsync(hash).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "Failed to drop cancelled launch torrent {Hash}", hash); }
            });
        }
    }

    private async Task WaitForPlayerWakeupAsync(string torrentHash, string torrentTitle, CancellationToken ct)
    {
        if (_internalPlayerServer == null)
        {
            await Task.Delay(2500, ct).ConfigureAwait(false);
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPlayerStateChanged(object? sender, InternalPlayerState state)
        {
            if (state.IsClosed) return;

            bool matches = false;
            if (!string.IsNullOrEmpty(torrentHash) && string.Equals(state.TorrentHash, torrentHash, StringComparison.OrdinalIgnoreCase))
            {
                matches = true;
            }
            else if (!string.IsNullOrEmpty(state.OriginalTitle) &&
                     (torrentTitle.Contains(state.OriginalTitle, StringComparison.OrdinalIgnoreCase) ||
                      state.OriginalTitle.Contains(torrentTitle, StringComparison.OrdinalIgnoreCase)))
            {
                matches = true;
            }
            else if (string.IsNullOrEmpty(torrentHash) && (state.IsLoading || state.IsBuffering || state.IsPlaying))
            {
                matches = true;
            }

            if (matches)
            {
                tcs.TrySetResult(true);
            }
        }

        _internalPlayerServer.PlayerStateChanged += OnPlayerStateChanged;
        using var reg = ct.Register(() => tcs.TrySetCanceled());

        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(25), ct)).ConfigureAwait(false);
            if (completed == tcs.Task)
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        finally
        {
            _internalPlayerServer.PlayerStateChanged -= OnPlayerStateChanged;
        }
    }

    private void LaunchStream(string streamUrl, TorrentEntity torrent, string fileName, string? torrentHash)
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
                torrentHash ?? string.Empty);

            _internalPlayerServer.SendMetadataAsync(initialMeta).SafeFireAndForget("Torrents.SendMetadata");
        }

        PlayerProcessHelper.LaunchPlayer(
            videoUrl: streamUrl,
            titleRu: titleRu,
            titleEn: titleEn,
            episode: episode,
            animeId: effectiveSelected?.Id,
            torrentHash: torrentHash,
            originalTitle: originalTitle);
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
