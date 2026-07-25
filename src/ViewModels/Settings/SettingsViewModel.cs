using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Core.Platform;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Services;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.Seasonal;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly MalAuthService _authService;
    private readonly ShikiAuthService _shikiAuthService;
    private readonly ShikiHostResolver _shikiHostResolver;
    private readonly AnimeListViewModel _animeListViewModel;
    private readonly LocalizationService _localizationService;
    private readonly UpdateService _updateService;
    private readonly CacheCleanupService _cacheCleanupService;
    private readonly ImageCacheService _imageCacheService;
    private readonly MappingService _mappingService;
    private readonly SeasonalViewModel _seasonalViewModel;

    
    // Per-mirror connection state. Only one Shiki mirror can be active at a time
    // (because their accounts/tokens are independent OAuth realms).
        public SettingsPlaybackViewModel Playback { get; }
    public SettingsUiViewModel Ui { get; }
    public SettingsSystemViewModel System { get; }

    public bool IsShikiOneConnected => _settingsService.Current.Api.Shiki?.Mirror == ShikiMirror.One;
    public bool IsShikiNetConnected => _settingsService.Current.Api.Shiki?.Mirror == ShikiMirror.Net;

    // A login button is clickable only when:
    //   - MAL is connected (master condition the user requested),
    //   - and the *other* mirror isn't already connected.
    public bool CanLoginShikiOne => IsLoggedIn && !IsShikiNetConnected;
    public bool CanLoginShikiNet => IsLoggedIn && !IsShikiOneConnected;

    
    

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiOne))]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiNet))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginOneCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginNetCommand))]
    private bool _isLoggedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiOne))]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiNet))]
    [NotifyPropertyChangedFor(nameof(IsShikiOneConnected))]
    [NotifyPropertyChangedFor(nameof(IsShikiNetConnected))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginOneCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginNetCommand))]
    private bool _isShikiLoggedIn;

    
    
    
    
    
    
    
    // Updates
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _newVersion;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    private int _updateProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    private bool _isUpdateDownloaded;

    public bool IsDownloadReady => IsUpdateAvailable && !IsUpdateDownloaded && UpdateProgress == 0;
    public bool IsDownloading => UpdateProgress > 0 && !IsUpdateDownloaded;

    // System
    
    
    
    
    
    
    
    
    
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClearSelectedCache))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectedCacheCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCacheStatsCommand))]
    private bool _isCacheBusy;

    [ObservableProperty]
    private string _cacheStatus = string.Empty;

    public ObservableCollection<CacheCleanupItem> CacheItems { get; } = new()
    {
        new CacheCleanupItem(CacheCleanupTarget.History, "settings.cache.items.history"),
        new CacheCleanupItem(CacheCleanupTarget.ImageFiles, "settings.cache.items.images"),
        new CacheCleanupItem(CacheCleanupTarget.ApiCache, "settings.cache.items.api"),
        new CacheCleanupItem(CacheCleanupTarget.RecognitionCache, "settings.cache.items.recognition"),
        new CacheCleanupItem(CacheCleanupTarget.SeasonalCache, "settings.cache.items.seasonal")
    };

    public bool CanClearSelectedCache => !IsCacheBusy && CacheItems.Any(x => x.IsSelected);

    // Notifications
    
    
    
    
    
    
    
    
    
    
    [ObservableProperty]
    private bool _singlePlayerWindow = true;

    [ObservableProperty]
    private string _mpvHwdec = "auto";

    [ObservableProperty]
    private string _mpvVideoOutput = "gpu-next";

    [ObservableProperty]
    private string _mpvGpuApi = "auto";

    [ObservableProperty]
    private string _mpvGpuContext = "auto";

    /// <summary>
    /// Live, two-way bound list of user-defined share buttons. Edits are
    /// persisted via <see cref="HookCustomLink"/> (item-level PropertyChanged)
    /// and <see cref="OnCustomLinksCollectionChanged"/> (add/remove). The
    /// underlying <c>_settingsService.Current.CustomLinks</c> list IS this
    /// collection's backing storage.
    /// </summary>
    public ObservableCollection<CustomShareLink> CustomLinks { get; } = new();

    private readonly AnisthesiaService _anisthesiaService;
    private readonly DiscordService _discordService;
    private readonly SystemIntegrationService _systemIntegrationService;
    private readonly FaviconService _faviconService;

    public SettingsViewModel(
        SettingsService settingsService,
        MalAuthService authService,
        ShikiAuthService shikiAuthService,
        ShikiHostResolver shikiHostResolver,
        AnimeListViewModel animeListViewModel,
        LocalizationService localizationService,
        UpdateService updateService,
        AnisthesiaService anisthesiaService,
        DiscordService discordService,
        CacheCleanupService cacheCleanupService,
        ImageCacheService imageCacheService,
        MappingService mappingService,
        SeasonalViewModel seasonalViewModel,
        SystemIntegrationService systemIntegrationService,
        FaviconService faviconService)
    {
        _settingsService = settingsService;
        _authService = authService;
        _shikiAuthService = shikiAuthService;
        _shikiHostResolver = shikiHostResolver;
        _animeListViewModel = animeListViewModel;
        _localizationService = localizationService;
        _updateService = updateService;
        _cacheCleanupService = cacheCleanupService;
        _anisthesiaService = anisthesiaService;
        _discordService = discordService;
        _imageCacheService = imageCacheService;
        _mappingService = mappingService;
        _seasonalViewModel = seasonalViewModel;
        _systemIntegrationService = systemIntegrationService;
        _faviconService = faviconService;

        // Update state
        IsUpdateAvailable = _updateService.IsUpdateAvailable;
        NewVersion = _updateService.NewVersion;

                // Load existing settings
        Playback = new SettingsPlaybackViewModel(_settingsService, _systemIntegrationService, _anisthesiaService);
        Ui = new SettingsUiViewModel(_settingsService, _animeListViewModel, _localizationService);
        System = new SettingsSystemViewModel(_settingsService, _discordService);
        
InitializeCustomLinks();
        InitializeCacheItems();
        _ = RefreshCacheStats();
    }

    private void InitializeCacheItems()
    {
        foreach (var item in CacheItems)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CacheCleanupItem.IsSelected))
                {
                    OnPropertyChanged(nameof(CanClearSelectedCache));
                    ClearSelectedCacheCommand.NotifyCanExecuteChanged();
                }
            };
        }
    }

}
