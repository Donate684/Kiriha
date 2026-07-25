using Kiriha.Services.Tracking.Integration;
using Kiriha.Services.Tracking.Feed;
using Kiriha.Services.Tracking.Core;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Mapping;
using Kiriha.Services.Data.Settings;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;
using Kiriha.Services;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.Seasonal;
using Kiriha.Core.Shiki;
using Kiriha.Core;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    public SettingsPlaybackViewModel Playback { get; }
    public SettingsUiViewModel Ui { get; }
    public SettingsSystemViewModel System { get; }
    
    public SettingsAuthViewModel Auth { get; }
    public SettingsUpdateViewModel Update { get; }
    public SettingsCacheViewModel Cache { get; }

    /// <summary>
    /// Live, two-way bound list of user-defined share buttons. Edits are
    /// persisted via HookCustomLink (item-level PropertyChanged)
    /// and OnCustomLinksCollectionChanged (add/remove). The
    /// underlying _settingsService.Current.CustomLinks list IS this
    /// collection's backing storage.
    /// </summary>
    public ObservableCollection<CustomShareLink> CustomLinks { get; } = new();

    private readonly SettingsService _settingsService;
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
        _faviconService = faviconService;

        Playback = new SettingsPlaybackViewModel(settingsService, systemIntegrationService, anisthesiaService);
        Ui = new SettingsUiViewModel(settingsService, animeListViewModel, localizationService);
        System = new SettingsSystemViewModel(settingsService, discordService);

        Auth = new SettingsAuthViewModel(settingsService, authService, shikiAuthService, shikiHostResolver);
        Update = new SettingsUpdateViewModel(updateService);
        Cache = new SettingsCacheViewModel(cacheCleanupService, imageCacheService, mappingService, seasonalViewModel);

        InitializeCustomLinks();
    }
}
