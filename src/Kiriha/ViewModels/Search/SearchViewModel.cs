using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Infrastructure;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Services.Data;
using Kiriha.Utils.Collections;
using Serilog;

namespace Kiriha.ViewModels.Search;

public partial class SearchViewModel : ViewModelBase, IDisposable
{
    private readonly Kiriha.Core.Abstractions.Services.IMalApiService _apiService;
    private readonly ShikiMetadataService _shikiMetadataService;
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly LoadQueueService _queueService;
    private readonly Kiriha.Core.Abstractions.Repositories.IAnimeRepository _animeRepo;
    private readonly Kiriha.Core.Abstractions.Services.ISyncManager _syncManager;
    private readonly Kiriha.Core.Dialogs.IDialogService _dialogService;
    private readonly Kiriha.Core.Abstractions.Services.ILocalizer _localizer;

    public Kiriha.Core.Dialogs.IDialogService DialogService => _dialogService;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hideInLists;
    [NotifyPropertyChangedFor(nameof(DisplayAdultFilter))]
    [ObservableProperty] private AdultFilterMode _adultFilter = AdultFilterMode.Hide;

    public AdultFilterMode[] AdultFilterOptions { get; } = [AdultFilterMode.Hide, AdultFilterMode.Include, AdultFilterMode.Only];
    public string DisplayAdultFilter => AdultFilter switch
    {
        AdultFilterMode.Hide => _localizer.GetLoc("filters.adult.hide"),
        AdultFilterMode.Include => _localizer.GetLoc("filters.adult.include"),
        AdultFilterMode.Only => _localizer.GetLoc("filters.adult.only"),
        _ => "18+"
    };

    [RelayCommand]
    public void CycleAdultFilter()
    {
        AdultFilter = AdultFilter switch
        {
            AdultFilterMode.Hide => AdultFilterMode.Include,
            AdultFilterMode.Include => AdultFilterMode.Only,
            AdultFilterMode.Only => AdultFilterMode.Hide,
            _ => AdultFilterMode.Hide
        };
    }

    public BulkObservableCollection<AnimeEntity> SearchResults { get; } = new();

    private CancellationTokenSource? _searchCts;
    private bool _isDisposed;
    private readonly Kiriha.Utils.Async.Debouncer _searchDebouncer;

    public SearchViewModel(Kiriha.Core.Abstractions.Services.IMalApiService apiService, ShikiMetadataService shikiMetadataService,
        Kiriha.Core.Abstractions.Services.ISettingsService settingsService, LoadQueueService queueService,
        Kiriha.Core.Abstractions.Repositories.IAnimeRepository animeRepo, Kiriha.Core.Abstractions.Services.ISyncManager syncManager, Kiriha.Core.Dialogs.IDialogService dialogService,
        Kiriha.Core.Abstractions.Services.ILocalizer localizer)
    {
        _apiService = apiService;
        _shikiMetadataService = shikiMetadataService;
        _settingsService = settingsService;
        _queueService = queueService;
        _animeRepo = animeRepo;
        _syncManager = syncManager;
        _dialogService = dialogService;
        _localizer = localizer;

        _searchDebouncer = new Kiriha.Utils.Async.Debouncer(TimeSpan.FromMilliseconds(800), _ =>
        {
            return Dispatcher.UIThread.InvokeAsync(() => PerformSearch());
        });
    }

    /// <summary>
    /// Called from view's ElementPrepared handler to lazily load images
    /// only for items that have entered the viewport.
    /// </summary>
    public void EnqueueItemForViewport(AnimeEntity item)
    {
        if (item == null) return;
        _queueService.EnqueueForViewport(new[] { item });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        var cts = Interlocked.Exchange(ref _searchCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        _searchDebouncer?.Dispose();
    }
}
