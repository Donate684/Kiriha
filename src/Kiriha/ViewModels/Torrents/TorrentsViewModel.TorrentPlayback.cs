using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Abstractions.Services;
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
        CancellationToken batchToken = default;
        TorrServerPreparation? batch = null;

        try
        {
            if (torrent.IsBatch)
            {
                var previous = _modalCts;
                _modalCts = new CancellationTokenSource();
                ReleaseEpisodePreparation();
                var ct = _modalCts.Token;
                batchToken = ct;
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

                batch = await _torrServer.PrepareTorrentAsync(link, torrent.Title, ct);
                ct.ThrowIfCancellationRequested();
                if (batch is null)
                {
                    EpisodeSelectorError = _localizer.GetLoc("torrents.streaming.add_failed");
                    IsEpisodeSelectorLoading = false;
                    return;
                }

                var files = await _torrServer.WaitForFilesAsync(batch.Status.Hash, TimeSpan.FromSeconds(35), ct);
                ct.ThrowIfCancellationRequested();
                var videoFiles = files.Where(f => f.IsVideo).ToList();

                if (videoFiles.Count == 0)
                {
                    EpisodeSelectorError = _localizer.GetLoc("torrents.streaming.no_video");
                    IsEpisodeSelectorLoading = false;
                    return;
                }

                _episodePreparation = batch;
                batch = null;
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

                using var prepared = await _torrServer.PrepareTorrentAsync(link, torrent.Title, ct);
                ct.ThrowIfCancellationRequested();
                if (prepared is null)
                {
                    Log.Warning("TorrentsViewModel: Failed to add torrent to TorrServer");
                    IsPlayerLaunching = false;
                    StreamingErrorMessage = _localizer.GetLoc("torrents.streaming.add_failed");
                    return;
                }

                var status = prepared.Status;
                var files = await _torrServer.WaitForFilesAsync(status.Hash, TimeSpan.FromSeconds(12), ct);
                ct.ThrowIfCancellationRequested();
                var videoFiles = files.Where(f => f.IsVideo).ToList();
                if (files.Count > 0 && videoFiles.Count == 0)
                {
                    Log.Warning("TorrentsViewModel: Torrent {Hash} resolved metadata ({Count} files) with no video files", status.Hash, files.Count);
                    IsPlayerLaunching = false;
                    StreamingErrorMessage = _localizer.GetLoc("torrents.streaming.no_video");
                    return;
                }

                var targetFile = videoFiles.OrderByDescending(f => f.Length).FirstOrDefault();
                var fileId = targetFile?.Id ?? -1;
                var fileName = targetFile?.FileName ?? (torrent.Title + ".mkv");

                await OpenStreamAsync(prepared, torrent, fileId, fileName, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TorrentsViewModel: Streaming error for '{Title}'", torrent.Title);
            if (torrent.IsBatch && !batchToken.IsCancellationRequested && IsEpisodeSelectorVisible)
            {
                EpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
                IsEpisodeSelectorLoading = false;
            }
            else if (!torrent.IsBatch && !launchToken.IsCancellationRequested)
            {
                StreamingErrorMessage = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
            }
        }
        finally
        {
            batch?.Dispose();
            if (!torrent.IsBatch)
                FinishPlayerLaunch(launchToken);
        }
    }

    [RelayCommand]
    public async Task SelectEpisodeFile(Kiriha.Core.Domain.Models.TorrServer.TorrServerFileItem file)
    {
        if (file is null || _episodePreparation is null || ActiveStreamingTorrent is null)
            return;

        var torrent = ActiveStreamingTorrent;
        using var prepared = _episodePreparation;
        _episodePreparation = null;
        CloseEpisodeSelector();

        var ct = BeginPlayerLaunch();

        IsPlayerLaunching = true;
        PlayerLaunchingTitle = $"{torrent.Title} - {file.FileName}";

        try
        {
            await OpenStreamAsync(prepared, torrent, file.Id, file.FileName, ct);
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
    public void CloseEpisodeSelector()
    {
        _modalCts?.Cancel();
        IsEpisodeSelectorVisible = false;
        IsEpisodeSelectorLoading = false;
        EpisodeSelectorError = null;
        EpisodeFiles.Clear();
        EpisodeSelectorCountText = string.Empty;
        ActiveStreamingTorrent = null;

        ReleaseEpisodePreparation();
    }

    private void ReleaseEpisodePreparation()
    {
        var prepared = _episodePreparation;
        _episodePreparation = null;
        prepared?.Dispose();
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
    }

    private async Task OpenStreamAsync(TorrServerPreparation prepared, TorrentEntity torrent, int fileId,
        string fileName, CancellationToken ct)
    {
        if (prepared.ServerUrl != _torrServer.ServerUrl)
        {
            StreamingErrorMessage = _localizer.GetLoc(AppConstants.Player.TorrServerStartFailedLocKey);
            return;
        }

        var torrentHash = prepared.Status.Hash;
        var url = _torrServer.GetStreamUrl(torrentHash, fileId, fileName);
        var opened = await LaunchAndWaitForPlayerAsync(url, torrent, fileName, torrentHash, ct);
        ct.ThrowIfCancellationRequested();
        if (!opened)
            StreamingErrorMessage = _localizer.GetLoc(AppConstants.Player.OpenFailedLocKey);
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
                torrentHash)
            {
                PlaybackRequestId = requestId
            };

            _internalPlayerServer.StagePlaybackMetadata(initialMeta);
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
