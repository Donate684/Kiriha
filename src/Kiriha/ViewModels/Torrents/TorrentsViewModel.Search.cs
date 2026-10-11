using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    partial void OnSelectedAnimeChanged(AnimeEntity? value)
    {
        foreach (var item in HideMenuItems)
        {
            item.IsSelected = value != null && item.Anime.Id == value.Id;
        }

        ReloadFiltersForCurrentContext();

        if (value != null)
        {
            if (UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery))
            {
                SearchQuery = CustomQuery;
            }
            else
            {
                SearchQuery = value.Title;
            }
            PerformSearchCommand.Execute(null);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (SelectedAnime != null)
        {
            bool isSameAsAnime = string.Equals(value, SelectedAnime.Title, System.StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(SelectedAnime.EnglishTitle) && string.Equals(value, SelectedAnime.EnglishTitle, System.StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(SelectedAnime.RussianTitle) && string.Equals(value, SelectedAnime.RussianTitle, System.StringComparison.OrdinalIgnoreCase))
                || (UseCustomQuery && !string.IsNullOrEmpty(CustomQuery) && string.Equals(value, CustomQuery, System.StringComparison.OrdinalIgnoreCase));

            if (!isSameAsAnime)
            {
                _selectedAnime = null;
                OnPropertyChanged(nameof(SelectedAnime));
                foreach (var item in HideMenuItems)
                {
                    item.IsSelected = false;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(value))
        {
            var matched = ResolveAnimeForQuery(value);
            if (matched != null)
            {
                _selectedAnime = matched;
                OnPropertyChanged(nameof(SelectedAnime));
                foreach (var item in HideMenuItems)
                {
                    item.IsSelected = item.Anime.Id == matched.Id;
                }
            }
        }
    }

    [RelayCommand]
    public async Task PerformSearch()
    {
        SyncFilterContext();

        string query = TorrentQueryBuilder.Build(SearchQuery, new TorrentQueryFilters(
            FilterVaryg,
            FilterEraiRaws,
            FilterToonsHub,
            FilterJudas,
            Filter1080p,
            FilterHevc,
            OnlyCrunchyroll,
            FilterNetflix,
            FilterAmazon,
            FilterHidive));

        if (string.IsNullOrEmpty(query))
        {
            Log.Debug("Torrents: Query is empty, clearing results");
            Torrents.Clear();
            return;
        }

        try
        {
            IsLoading = true;
            Log.Information("Torrents: Starting search for: {Query}", query);

            var results = await _rssService.SearchTorrentsAsync(query);
            Log.Information("Torrents: Search returned {Count} items", results.Count);

            Torrents.Reset(results);

            if (results.Count == 0)
            {
                Log.Warning("Torrents: No results found for: {Query}", query);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Torrents: Search failed for: {Query}", query);
        }
        finally
        {
            IsLoading = false;
            Log.Debug("Torrents: Search process completed (loading set to false)");
        }
    }

    [RelayCommand]
    public void SelectAnime(AnimeEntity? anime)
    {
        if (SelectedAnime == anime && anime != null)
        {
            SearchQuery = UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
                ? CustomQuery
                : anime.Title;
            PerformSearchCommand.Execute(null);
            return;
        }
        SelectedAnime = anime;
    }

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
    public async Task PlayTorrentAsync(TorrentEntity torrent)
    {
        if (torrent is null) return;
        var link = !string.IsNullOrWhiteSpace(torrent.MagnetLink) ? torrent.MagnetLink : torrent.DownloadLink;
        if (string.IsNullOrWhiteSpace(link)) return;

        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            if (torrent.IsBatch)
            {
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

                var files = await _torrServer.WaitForFilesAsync(status.Hash, TimeSpan.FromSeconds(25), ct);
                var videoFiles = files.Where(f => f.IsVideo).ToList();

                if (videoFiles.Count == 0)
                {
                    EpisodeSelectorError = _localizer.GetLoc("torrents.streaming.no_video");
                    IsEpisodeSelectorLoading = false;
                    return;
                }

                foreach (var f in videoFiles)
                {
                    EpisodeFiles.Add(f);
                }
                EpisodeSelectorCountText = _localizer.GetLoc("torrents.results.actions.episodes_count", EpisodeFiles.Count);
                IsEpisodeSelectorLoading = false;
            }
            else
            {
                IsPlayerLaunching = true;
                PlayerLaunchingTitle = torrent.Title;

                var isReady = await _torrServer.EnsureRunningAsync(ct);
                if (!isReady)
                {
                    Log.Warning("TorrentsViewModel: TorrServer is not ready");
                    IsPlayerLaunching = false;
                    return;
                }

                var status = await _torrServer.AddTorrentAsync(link, torrent.Title, ct);
                if (status is null)
                {
                    Log.Warning("TorrentsViewModel: Failed to add torrent to TorrServer");
                    IsPlayerLaunching = false;
                    return;
                }

                _currentStreamingHash = status.Hash;

                // Wait up to 15 seconds for TorrServer to connect to peers and resolve torrent metadata
                var files = await _torrServer.WaitForFilesAsync(status.Hash, TimeSpan.FromSeconds(15), ct);
                var videoFiles = files.Where(f => f.IsVideo).ToList();
                var targetFile = videoFiles.OrderByDescending(f => f.Length).FirstOrDefault();

                var fileId = targetFile?.Id ?? -1;
                var fileName = targetFile?.FileName ?? (torrent.Title + ".mkv");

                var streamUrl = _torrServer.GetStreamUrl(status.Hash, fileId, fileName);
                LaunchStream(streamUrl, torrent, fileName);

                await WaitForPlayerWakeupAsync(status.Hash, torrent.Title, ct).ConfigureAwait(false);
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
                EpisodeSelectorError = string.Format(_localizer.GetLoc("torrents.streaming.playback_error"), ex.Message);
                IsEpisodeSelectorLoading = false;
            }
        }
        finally
        {
            if (!torrent.IsBatch)
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
        var streamUrl = _torrServer.GetStreamUrl(_currentStreamingHash, file.Id, file.FileName);
        CloseEpisodeSelector();

        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        IsPlayerLaunching = true;
        PlayerLaunchingTitle = $"{torrent.Title} - {file.FileName}";

        try
        {
            LaunchStream(streamUrl, torrent, file.FileName);
            await WaitForPlayerWakeupAsync(_currentStreamingHash, torrent.Title, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsPlayerLaunching = false);
        }
    }

    [RelayCommand]
    public void CloseEpisodeSelector()
    {
        _streamingCts?.Cancel();
        IsEpisodeSelectorVisible = false;
        IsEpisodeSelectorLoading = false;
        EpisodeSelectorError = null;
        EpisodeFiles.Clear();
        EpisodeSelectorCountText = string.Empty;
        ActiveStreamingTorrent = null;
    }

    [RelayCommand]
    public void CancelStreaming()
    {
        CloseEpisodeSelector();
    }

    [RelayCommand]
    public void CancelPlayerLaunching()
    {
        _streamingCts?.Cancel();
        IsPlayerLaunching = false;
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
            else if (state.IsLoading || state.IsBuffering || state.IsPlaying)
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
            // Keep "Запуск плеера..." dialog active until player process responds over IPC (up to 25s timeout)
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

    private void LaunchStream(string streamUrl, TorrentEntity torrent, string fileName)
    {
        var originalTitle = !string.IsNullOrWhiteSpace(fileName) ? fileName : torrent.Title;
        var episode = !string.IsNullOrWhiteSpace(torrent.Episode) ? torrent.Episode : null;
        var titleRu = SelectedAnime?.RussianTitle ?? (!string.IsNullOrWhiteSpace(torrent.AnimeTitle) ? torrent.AnimeTitle : string.Empty);
        var titleEn = SelectedAnime?.Title ?? torrent.Title;

        if (_internalPlayerServer != null)
        {
            var initialMeta = new PlayerMediaMetadata(
                originalTitle,
                titleRu,
                titleEn,
                episode ?? string.Empty,
                SelectedAnime?.Id,
                titleEn,
                _currentStreamingHash ?? string.Empty);

            _internalPlayerServer.SendMetadataAsync(initialMeta).SafeFireAndForget("Torrents.SendMetadata");
        }

        PlayerProcessHelper.LaunchPlayer(
            videoUrl: streamUrl,
            titleRu: titleRu,
            titleEn: titleEn,
            episode: episode,
            animeId: SelectedAnime?.Id,
            torrentHash: _currentStreamingHash,
            originalTitle: originalTitle);
    }

    [RelayCommand]
    public void ClearSelectedAnime()
    {
        SelectedAnime = null;
        SearchQuery = string.Empty;
        Torrents.Clear();
        RebuildGroupedTorrents();
    }
}

internal readonly record struct TorrentQueryFilters(
    bool FilterVaryg,
    bool FilterEraiRaws,
    bool FilterToonsHub,
    bool FilterJudas,
    bool Filter1080p,
    bool FilterHevc,
    bool OnlyCrunchyroll,
    bool FilterNetflix,
    bool FilterAmazon,
    bool FilterHidive);

internal static class TorrentQueryBuilder
{
    public static string Build(string? baseQuery, TorrentQueryFilters filters)
    {
        var query = baseQuery?.Trim() ?? string.Empty;

        query = Append(query, filters.FilterVaryg, "VARYG");
        query = Append(query, filters.FilterEraiRaws, "Erai-raws");
        query = Append(query, filters.FilterToonsHub, "ToonsHub");
        query = Append(query, filters.FilterJudas, "Judas");
        query = Append(query, filters.Filter1080p, "1080p");
        query = Append(query, filters.FilterHevc, "HEVC");
        query = Append(query, filters.OnlyCrunchyroll, "CR");
        query = Append(query, filters.FilterNetflix, "NF");
        query = Append(query, filters.FilterAmazon, "AMZN");
        query = Append(query, filters.FilterHidive, "HIDIVE");

        return query;
    }

    private static string Append(string query, bool enabled, string token)
    {
        if (!enabled) return query;
        return string.IsNullOrEmpty(query) ? token : $"{query} {token}";
    }
}


