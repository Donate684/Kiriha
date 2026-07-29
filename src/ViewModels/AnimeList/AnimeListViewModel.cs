using Kiriha.Services.Tracking.Integration;
using Kiriha.Services.Tracking.Feed;
using Kiriha.Services.Tracking.Core;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Sync;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AnimeRepository _animeRepo;
    private readonly AnimeSyncOrchestrator _syncOrchestrator;
    private readonly AnimeProgressService _progressService;
    private readonly LoadQueueService _queueService;
    private readonly AiringInfoService _airingInfoService;
    private readonly RssFeedService _rssService;
    private readonly AppReadinessService _readinessService;
    private readonly Kiriha.Core.Dialogs.IDialogService _dialogService;
    private readonly ShikiMetadataService _shikiMetadataService;
    private readonly AnimeCollectionProjection _listProjection = new();

    public SettingsService SettingsService => _settingsService;
    public Kiriha.Core.Dialogs.IDialogService DialogService => _dialogService;
    public ShikiMetadataService ShikiMetadataService => _shikiMetadataService;

    public ObservableCollection<AnimeItem> AnimeItems => _animeRepo.Collection;

    public AnimeListViewModel(
        SettingsService settingsService,
        AnimeRepository animeRepo,
        AnimeSyncOrchestrator syncOrchestrator,
        AnimeProgressService progressService,
        LoadQueueService queueService,
        AiringInfoService airingInfoService,
        RssFeedService rssService,
        AppReadinessService readinessService,
        Kiriha.Core.Dialogs.IDialogService dialogService,
        ShikiMetadataService shikiMetadataService)
    {
        _settingsService = settingsService;
        _animeRepo = animeRepo;
        _syncOrchestrator = syncOrchestrator;
        _progressService = progressService;
        _queueService = queueService;
        _airingInfoService = airingInfoService;
        _rssService = rssService;
        _readinessService = readinessService;
        _dialogService = dialogService;
        _shikiMetadataService = shikiMetadataService;

        _filterNsfw = _settingsService.Current.UI.ListShowNsfw;
        _sortBy = _settingsService.Current.UI.ListSortBy;
        IsFilterActive = _filterNsfw;

        _filterRefreshDebouncer = new Kiriha.Utils.Async.Debouncer(
            TimeSpan.FromMilliseconds(180),
            ApplyCurrentFiltersAsync);

        _searchDebouncer = new Kiriha.Utils.Async.Debouncer(
            TimeSpan.FromMilliseconds(300),
            _ =>
            {
                ScheduleFilterRefresh();
                return Task.CompletedTask;
            });

        _collectionChangeDebouncer = new Kiriha.Utils.Async.Debouncer(TimeSpan.FromMilliseconds(200), async ct =>
        {
            await UpdateCountsAsync();
            await ApplyCurrentFiltersAsync(ct);
        });

        _animeRepo.Collection.CollectionChanged += OnCollectionChanged;

        _airingTicker = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, OnAiringTick);
        _airingTicker.Start();

        WeakReferenceMessenger.Default.Register<AnimeListRefreshMessage>(this, (r, m) =>
        {
            Dispatcher.UIThread.Post(() => ((AnimeListViewModel)r).RefreshAfterDetailsEdit());
        });

        RefreshLocalization();
        _readinessService.StateChanged += OnReadinessStateChanged;
        ObserveReadinessAsync().SafeFireAndForget("ObserveReadinessAsync");
    }

    partial void OnSortByChanged(string value)
    {
        _settingsService.Update(settings => settings.UI.ListSortBy = value, SettingsSection.UI);
        ScheduleFilterRefresh();
    }

    partial void OnFilterNsfwChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.ListShowNsfw = value, SettingsSection.UI);
        IsFilterActive = value;
        ScheduleFilterRefresh();
    }



    public void Dispose()
    {
        _searchDebouncer?.Dispose();
        _collectionChangeDebouncer?.Dispose();
        _filterRefreshDebouncer?.Dispose();
        _airingTicker?.Stop();
        _airingTicker = null;
        _readinessService.StateChanged -= OnReadinessStateChanged;
        _animeRepo.Collection.CollectionChanged -= OnCollectionChanged;
        _listProjection.Dispose();
    }
}
