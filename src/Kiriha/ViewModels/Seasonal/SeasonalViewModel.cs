using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data.Core;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel : ViewModelBase, IDisposable
{
    private readonly IMalApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly LoadQueueService _queueService;
    private readonly IAnimeRepository _animeRepo;
    private readonly SeasonalCacheStore _cacheStore;
    private readonly IAnimeListActionService _listActionService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizer _localizer;
    private readonly IFranchiseService _franchiseService;
    private readonly IMetadataRepository _metadataRepo;
    private readonly ISeasonalHiddenRepository _seasonalHiddenRepo;
    private readonly IAnimeCountryService _countryService;

    public IDialogService DialogService => _dialogService;

    public SeasonalViewModel(
        IMalApiService apiService,
        ISettingsService settingsService,
        LoadQueueService queueService,
        IAnimeRepository animeRepo,
        SeasonalCacheStore cacheStore,
        IAnimeListActionService listActionService,
        IDialogService dialogService,
        ILocalizer localizer,
        IFranchiseService franchiseService,
        IMetadataRepository metadataRepo,
        ISeasonalHiddenRepository seasonalHiddenRepo,
        IAnimeCountryService countryService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _queueService = queueService;
        _animeRepo = animeRepo;
        _cacheStore = cacheStore;
        _listActionService = listActionService;
        _dialogService = dialogService;
        _localizer = localizer;
        _franchiseService = franchiseService;
        _metadataRepo = metadataRepo;
        _seasonalHiddenRepo = seasonalHiddenRepo;
        _countryService = countryService;

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
                    .DistinctBy(x => x.Id)
                    .ToDictionary(x => x.Id, x => x.Status);
                vm.UpdateUserList(userStore);
            });
        });

        _franchiseService.IndexRebuilt += OnFranchiseIndexRebuilt;

        RefreshLocalization();
        _isInitializing = false;
        ScheduleDeferredInitialLoad();
    }

    private void OnFranchiseIndexRebuilt()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DisplayItems == null || DisplayItems.Count == 0) return;
            var index = _franchiseService.GetIndex();
            foreach (var item in DisplayItems)
            {
                var newCtx = index.TryGetValue(item.Id, out var ctx) ? ctx : null;
                if (item.Franchise != newCtx)
                {
                    item.Franchise = newCtx;
                }
            }
        });
    }
}
