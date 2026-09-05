using Kiriha.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Services.Data.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Core.Tracking.Sync;
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
using Kiriha.Infrastructure;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;
using Kiriha.Utils.Async;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IAnimeRepository _animeRepo;
    private readonly IAnimeSyncOrchestrator _syncOrchestrator;
    private readonly IProgressUpdateService _progressService;
    private readonly ILoadQueueService _queueService;
    private readonly IAiringInfoService _airingInfoService;
    private readonly RssFeedService _rssService;
    private readonly AppReadinessService _readinessService;
    private readonly IDialogService _dialogService;
    private readonly ShikiMetadataService _shikiMetadataService;
    private readonly AnimeCollectionProjection _listProjection = new();
    private readonly ILocalizer _localizer;

    public ISettingsService SettingsService => _settingsService;
    public IDialogService DialogService => _dialogService;
    public ShikiMetadataService ShikiMetadataService => _shikiMetadataService;

    public ObservableCollection<AnimeEntity> AnimeItems => _animeRepo.Collection;

    public AnimeListViewModel(
        ISettingsService settingsService,
        IAnimeRepository animeRepo,
        IAnimeSyncOrchestrator syncOrchestrator,
        IProgressUpdateService progressService,
        ILoadQueueService queueService,
        IAiringInfoService airingInfoService,
        RssFeedService rssService,
        AppReadinessService readinessService,
        IDialogService dialogService,
        ShikiMetadataService shikiMetadataService,
        ILocalizer localizer)
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
        _localizer = localizer;

        _filterNsfw = _settingsService.Current.UI.ListShowNsfw;
        _sortBy = _settingsService.Current.UI.ListSortBy;
        _prioritizeNewEpisodes = _settingsService.Current.UI.ListPrioritizeNewEpisodes;
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

    partial void OnPrioritizeNewEpisodesChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.ListPrioritizeNewEpisodes = value, SettingsSection.UI);
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

