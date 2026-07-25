using Kiriha.Services.Tracking.Integration;
using Kiriha.Services.Tracking.Feed;
using Kiriha.Services.Tracking.Core;
using Kiriha.Services.Data.Mapping;
using Kiriha.Core.Infrastructure;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repository;

namespace Kiriha.Services.Tracking.Feed;

public partial class RssFeedService
{
    private readonly NyaaFeedClient _nyaaClient;
    private readonly AnimeRepository _animeRepo;
    private readonly MappingService _mappingService;
    private readonly IUiDispatcher _uiDispatcher;

    public System.Collections.ObjectModel.ObservableCollection<Kiriha.Models.TorrentItem> TorrentItems { get; } = new();

    public RssFeedService(
        NyaaFeedClient nyaaClient,
        AnimeRepository animeRepo,
        MappingService mappingService,
        IUiDispatcher uiDispatcher)
    {
        _nyaaClient = nyaaClient;
        _animeRepo = animeRepo;
        _mappingService = mappingService;
        _uiDispatcher = uiDispatcher;
    }
}

