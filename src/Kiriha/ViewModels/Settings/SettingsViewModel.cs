using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Integration;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Mapping;
using Kiriha.Services.Data.Settings;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Services;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.Seasonal;
using Kiriha.Core.Shared.Shiki;
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

    public SettingsCustomLinksViewModel CustomLinks { get; }

    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly FaviconService _faviconService;

    public SettingsViewModel(
        Kiriha.Core.Abstractions.Services.ISettingsService settingsService,
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

        CustomLinks = new SettingsCustomLinksViewModel(settingsService, faviconService);
    }
}
