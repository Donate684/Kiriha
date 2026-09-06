using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.Tracking;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking;
using Kiriha.Localization;
using Kiriha.Models;
using Serilog;

namespace Kiriha.ViewModels.Analytics;

public partial class MalDeepParserViewModel : ViewModelBase
{
    private readonly IAnimeRepository _animeRepo;
    private readonly ISyncManager _syncManager;
    private readonly IMalHistoryDeepParserService _parserService;
    private readonly ILocalizer _localizer;

    private CancellationTokenSource? _cts;
    private readonly Random _random = new();

    [ObservableProperty] private string _cookie = string.Empty;
    [ObservableProperty] private bool _onlyWithoutDates = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _currentTitle = string.Empty;
    [ObservableProperty] private string? _currentPoster;
    [ObservableProperty] private string _lastResultText = string.Empty;
    [ObservableProperty] private int _updatedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public MalDeepParserViewModel(
        IAnimeRepository animeRepo,
        ISyncManager syncManager,
        IMalHistoryDeepParserService parserService,
        ILocalizer localizer)
    {
        _animeRepo = animeRepo;
        _syncManager = syncManager;
        _parserService = parserService;
        _localizer = localizer;
    }

    [RelayCommand]
    public async Task StartParsing()
    {
        if (IsRunning) return;
        if (string.IsNullOrWhiteSpace(Cookie))
        {
            StatusMessage = _localizer.GetLoc("analytics.inspector.deep_parser_error_cookie");
            return;
        }

        var allItems = _animeRepo.Collection.ToList();
        List<AnimeEntity> targets;

        if (OnlyWithoutDates)
        {
            targets = allItems.Where(x =>
                x.Id > 0 &&
                (x.DateStarted == null || (x.Status == UserAnimeStatus.Completed && x.DateCompleted == null)))
                .ToList();
        }
        else
        {
            targets = allItems.Where(x => x.Id > 0).ToList();
        }

        if (targets.Count == 0)
        {
            StatusMessage = _localizer.GetLoc("analytics.inspector.deep_parser_no_history");
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsRunning = true;
        IsPaused = false;
        TotalCount = targets.Count;
        CurrentIndex = 0;
        UpdatedCount = 0;
        SkippedCount = 0;
        ErrorCount = 0;
        StatusMessage = string.Empty;
        bool authFailed = false;

        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                while (IsPaused)
                {
                    await Task.Delay(250, ct);
                }

                var item = targets[i];
                CurrentIndex = i + 1;
                ProgressPercentage = TotalCount > 0 ? (CurrentIndex * 100.0 / TotalCount) : 0;
                ProgressText = string.Format(_localizer.GetLoc("analytics.inspector.deep_parser_progress"), CurrentIndex, TotalCount);
                CurrentTitle = item.RussianTitle ?? item.Title;
                CurrentPoster = item.MainPictureUrl;

                try
                {
                    var isManga = item.MediaKind == MediaKind.Manga || item.MediaKind == MediaKind.LightNovel;
                    var result = await _parserService.FetchLatestWatchDatesAsync(item.Id, Cookie, isManga, ct);
                    if (result != null && (result.Value.StartDate.HasValue || result.Value.EndDate.HasValue))
                    {
                        var start = result.Value.StartDate;
                        var end = result.Value.EndDate;

                        bool changed = false;
                        if (start.HasValue && item.DateStarted != start.Value)
                        {
                            item.DateStarted = start.Value;
                            changed = true;
                        }

                        if (end.HasValue && item.Status == UserAnimeStatus.Completed && item.DateCompleted != end.Value)
                        {
                            item.DateCompleted = end.Value;
                            changed = true;
                        }

                        if (changed)
                        {
                            await _animeRepo.AddOrUpdateAnimeAsync(item);
                            await _syncManager.EnqueueFullUpdateAsync(item);
                            UpdatedCount++;
                        }
                        else
                        {
                            SkippedCount++;
                        }

                        if (start.HasValue && end.HasValue)
                        {
                            LastResultText = string.Format(_localizer.GetLoc("analytics.inspector.deep_parser_found_dates"), start.Value, end.Value);
                        }
                        else if (start.HasValue)
                        {
                            LastResultText = string.Format(_localizer.GetLoc("analytics.inspector.deep_parser_found_start"), start.Value);
                        }
                    }
                    else
                    {
                        SkippedCount++;
                        LastResultText = _localizer.GetLoc("analytics.inspector.deep_parser_no_history");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    ErrorCount++;
                    StatusMessage = _localizer.GetLoc("analytics.inspector.deep_parser_error_cookie");
                    LastResultText = _localizer.GetLoc("analytics.inspector.deep_parser_error_cookie");
                    authFailed = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Deep parser error on anime {Id}", item.Id);
                    ErrorCount++;
                }

                // Polite throttle (1000 - 1350ms) to avoid Cloudflare WAF block
                var delay = 1000 + _random.Next(0, 350);
                await Task.Delay(delay, ct);
            }

            if (!authFailed)
            {
                StatusMessage = _localizer.GetLoc("analytics.inspector.deep_parser_completed");
            }
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localizer.GetLoc("analytics.inspector.deep_parser_stop");
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
        }
    }

    [RelayCommand]
    public void TogglePause()
    {
        IsPaused = !IsPaused;
    }

    [RelayCommand]
    public void Stop()
    {
        _cts?.Cancel();
    }
}
