using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using System;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Tracking.Api;
using Kiriha.Services.Data;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel : ViewModelBase, IDisposable
{
    private readonly Kiriha.Core.Abstractions.Services.IMalApiService _apiService;
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly LoadQueueService _queueService;
    private readonly Kiriha.Core.Abstractions.Repositories.IAnimeRepository _animeRepo;
    private readonly SeasonalCacheStore _cacheStore;
    private readonly Kiriha.Core.Abstractions.Services.ISyncManager _syncManager;
    private readonly Kiriha.Core.Dialogs.IDialogService _dialogService;
    private readonly Kiriha.Core.Abstractions.Services.ILocalizer _localizer;

    public Kiriha.Core.Dialogs.IDialogService DialogService => _dialogService;

    public SeasonalViewModel(
        Kiriha.Core.Abstractions.Services.IMalApiService apiService,
        Kiriha.Core.Abstractions.Services.ISettingsService settingsService,
        LoadQueueService queueService,
        Kiriha.Core.Abstractions.Repositories.IAnimeRepository animeRepo,
        SeasonalCacheStore cacheStore,
        Kiriha.Core.Abstractions.Services.ISyncManager syncManager,
        Kiriha.Core.Dialogs.IDialogService dialogService,
        Kiriha.Core.Abstractions.Services.ILocalizer localizer)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _queueService = queueService;
        _animeRepo = animeRepo;
        _cacheStore = cacheStore;
        _syncManager = syncManager;
        _dialogService = dialogService;
        _localizer = localizer;

        HydrateDiskCacheOnce();
        LoadSettingsState();
        SetCurrentSeasonFromClock();

        _filterDebouncer = CreateSettingsDebouncer();
        _applyFilterDebouncer = new Kiriha.Utils.Async.Debouncer(TimeSpan.FromMilliseconds(300), () =>
        {
            ApplyFiltersAsync().SafeFireAndForget("ApplyFiltersAsync");
        });

        WeakReferenceMessenger.Default.Register<AnimeListRefreshMessage>(this, (r, m) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var vm = (SeasonalViewModel)r;
                var userStore = vm._animeRepo.Collection
                    .GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.First().Status);
                vm.UpdateUserList(userStore);
            });
        });

        RefreshLocalization();
        _isInitializing = false;
        ScheduleDeferredInitialLoad();
    }
}
