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
            var nextQuery = !IsOnlineMode && UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
                ? CustomQuery
                : GetAnimeDefaultQuery(value);
            SetSearchQueryFromContext(nextQuery);
            PerformSearchCommand.Execute(null);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (_updatingSearchQueryFromContext) return;

        if (SelectedAnime != null)
        {
            bool isSameAsAnime = string.Equals(value, SelectedAnime.Title, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(SelectedAnime.EnglishTitle) && string.Equals(value, SelectedAnime.EnglishTitle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(SelectedAnime.RussianTitle) && string.Equals(value, SelectedAnime.RussianTitle, StringComparison.OrdinalIgnoreCase))
                || (UseCustomQuery && !string.IsNullOrEmpty(CustomQuery) && string.Equals(value, CustomQuery, StringComparison.OrdinalIgnoreCase));

            if (!isSameAsAnime)
            {
                _selectedAnime = null;
                OnPropertyChanged(nameof(SelectedAnime));
                OnPropertyChanged(nameof(HasSelectedAnime));
                foreach (var item in HideMenuItems)
                {
                    item.IsSelected = false;
                }

                _suppressFilterPersist = true;
                try
                {
                    UseCustomQuery = false;
                }
                finally
                {
                    _suppressFilterPersist = false;
                }

                var matched = ResolveAnimeForQuery(value);
                if (matched != null)
                {
                    _selectedAnime = matched;
                    OnPropertyChanged(nameof(SelectedAnime));
                    OnPropertyChanged(nameof(HasSelectedAnime));
                    foreach (var item in HideMenuItems)
                    {
                        item.IsSelected = item.Anime.Id == matched.Id;
                    }
                }

                ReloadFiltersForCurrentContext();
            }
        }
        else if (!string.IsNullOrWhiteSpace(value))
        {
            var matched = ResolveAnimeForQuery(value);
            if (matched != null)
            {
                _selectedAnime = matched;
                OnPropertyChanged(nameof(SelectedAnime));
                OnPropertyChanged(nameof(HasSelectedAnime));
                foreach (var item in HideMenuItems)
                {
                    item.IsSelected = item.Anime.Id == matched.Id;
                }
                ReloadFiltersForCurrentContext();
            }
            else if (UseCustomQuery && !string.Equals(value, CustomQuery, StringComparison.OrdinalIgnoreCase))
            {
                _suppressFilterPersist = true;
                try
                {
                    UseCustomQuery = false;
                }
                finally
                {
                    _suppressFilterPersist = false;
                }
                ReloadFiltersForCurrentContext();
            }
        }
        else if (_currentFilterContextId != 0)
        {
            _suppressFilterPersist = true;
            try
            {
                UseCustomQuery = false;
            }
            finally
            {
                _suppressFilterPersist = false;
            }
            ReloadFiltersForCurrentContext();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task PerformSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (IsOnlineMode)
        {
            await PerformOnlineSearchAsync(SearchQuery, ct);
            return;
        }

        SyncFilterContext();

        var baseQuery = UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
            ? CustomQuery
            : SearchQuery;

        string query = TorrentQueryBuilder.Build(baseQuery, new TorrentQueryFilters(
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

            var results = await _rssService.SearchTorrentsAsync(query, ct);
            if (ct.IsCancellationRequested) return;

            ApplySelectedAnimeMatches(results);

            Log.Information("Torrents: Search returned {Count} items", results.Count);

            Torrents.Reset(results);

            if (results.Count == 0)
            {
                Log.Warning("Torrents: No results found for: {Query}", query);
            }
        }
        catch (OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Torrents: Search failed for: {Query}", query);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                Log.Debug("Torrents: Search process completed (loading set to false)");
            }
        }
    }

    private void ApplySelectedAnimeMatches(List<TorrentEntity> results)
    {
        if (SelectedAnime is not { Status: UserAnimeStatus.Watching or UserAnimeStatus.PlanToWatch } selected)
            return;

        if (!UseCustomQuery || string.IsNullOrWhiteSpace(CustomQuery))
            return;

        var normCustom = NormalizeTitle(CustomQuery);
        if (string.IsNullOrEmpty(normCustom))
            return;

        foreach (var torrent in results)
        {
            if (torrent.IsMatched || torrent.IsBatch)
                continue;

            if (!int.TryParse(torrent.Episode, out var epNum) || epNum <= selected.Progress)
                continue;

            var normTorrent = NormalizeTitle(torrent.AnimeTitle);
            if (!string.IsNullOrEmpty(normTorrent) &&
                (normTorrent.StartsWith(normCustom, StringComparison.Ordinal) ||
                 normCustom.StartsWith(normTorrent, StringComparison.Ordinal)))
            {
                torrent.IsMatched = true;
            }
        }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SelectedAnime = null;
        _currentFilterContextId = 0;
        _customFilterAnchorQuery = string.Empty;
        SetSearchQueryFromContext(string.Empty);
        ReloadFiltersForCurrentContext();
    }

    [RelayCommand]
    public void SelectAnime(AnimeEntity? anime)
    {
        if (SelectedAnime == anime && anime != null)
        {
            var nextQuery = !IsOnlineMode && UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
                ? CustomQuery
                : GetAnimeDefaultQuery(anime);
            SetSearchQueryFromContext(nextQuery);
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

                var files = await _torrServer.WaitForFilesAsync(status.Hash, TimeSpan.FromSeconds(25), ct);
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
                if (videoFiles.Count == 0)
                {
                    Log.Warning("TorrentsViewModel: No video files found in torrent {Hash} (0 seeds or dead magnet)", status.Hash);
                    await _torrServer.DropTorrentAsync(status.Hash, ct).ConfigureAwait(false);
                    _currentStreamingHash = null;
                    IsPlayerLaunching = false;
                    EpisodeSelectorError = _localizer.GetLoc("torrents.streaming.no_video");
                    return;
                }

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
                EpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
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
        CloseEpisodeSelector(dropTorrent: false);

        _launchCts?.Cancel();
        _launchCts?.Dispose();
        _launchCts = new CancellationTokenSource();
        var ct = _launchCts.Token;

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
        if (!string.IsNullOrEmpty(_currentStreamingHash))
        {
            var hash = _currentStreamingHash;
            _currentStreamingHash = null;
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
                _currentStreamingHash ?? string.Empty);

            _internalPlayerServer.SendMetadataAsync(initialMeta).SafeFireAndForget("Torrents.SendMetadata");
        }

        PlayerProcessHelper.LaunchPlayer(
            videoUrl: streamUrl,
            titleRu: titleRu,
            titleEn: titleEn,
            episode: episode,
            animeId: effectiveSelected?.Id,
            torrentHash: _currentStreamingHash,
            originalTitle: originalTitle);
    }

    private bool IsSelectedAnimeCompatibleWithTorrent(AnimeEntity? selected, TorrentEntity torrent, string originalTitle)
    {
        if (selected is null || selected.MediaKind != MediaKind.Anime)
            return false;

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(originalTitle);
        if (string.IsNullOrWhiteSpace(parsed.Title))
            parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(torrent.Title);

        if (parsed.Year.HasValue)
        {
            int? sy = selected.StartYear ?? selected.AiringDate?.Year;
            if (sy.HasValue && System.Math.Abs(sy.Value - parsed.Year.Value) > 1)
                return false;

            if (!sy.HasValue)
            {
                string ys = parsed.Year.Value.ToString();
                bool titleHasYear = selected.Title.Contains(ys, StringComparison.Ordinal) ||
                                    (selected.EnglishTitle?.Contains(ys, StringComparison.Ordinal) == true) ||
                                    (selected.RussianTitle?.Contains(ys, StringComparison.Ordinal) == true);
                if (!titleHasYear)
                    return false;
            }
        }

        if (!UseCustomQuery && parsed.Season is > 1)
        {
            int s = parsed.Season.Value;
            string normSel = Kiriha.Utils.Parsing.AnimeStringHelper.Normalize(selected.Title);
            string normEn = Kiriha.Utils.Parsing.AnimeStringHelper.Normalize(selected.EnglishTitle ?? string.Empty);
            string seasonToken = s.ToString();
            bool hasSeasonMarker = normSel.Split(' ').Contains(seasonToken) ||
                                   normEn.Split(' ').Contains(seasonToken) ||
                                   selected.AlternativeTitles.Any(alt => Kiriha.Utils.Parsing.AnimeStringHelper.Normalize(alt).Split(' ').Contains(seasonToken));
            if (!hasSeasonMarker)
                return false;
        }

        if (!UseCustomQuery &&
            int.TryParse(torrent.Episode, out int epNum) &&
            selected.TotalEpisodes > 1 &&
            epNum > selected.TotalEpisodes)
        {
            return false;
        }

        return true;
    }

    [RelayCommand]
    public void ClearSelectedAnime()
    {
        SelectedAnime = null;
        _currentFilterContextId = 0;
        _customFilterAnchorQuery = string.Empty;
        SetSearchQueryFromContext(string.Empty);
        ReloadFiltersForCurrentContext();
        Torrents.Clear();
        OnlineResults.Clear();
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


