using Kiriha.Models.Entities;
using Kiriha.Models.Entities;
using Kiriha.Core.Repositories;
using Kiriha.Core.Services;
using Kiriha.Core.Tracking.Integration;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Services;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Services;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repository;

namespace Kiriha.Core.Tracking.Feed;

public partial class RssFeedService
{
    private readonly NyaaFeedClient _nyaaClient;
    private readonly IAnimeRepository _animeRepo;
    private readonly IMappingService _mappingService;
    private readonly IUiDispatcher _uiDispatcher;

    public System.Collections.ObjectModel.ObservableCollection<TorrentEntity> TorrentItems { get; } = new();

    public RssFeedService(
        NyaaFeedClient nyaaClient,
        IAnimeRepository animeRepo,
        IMappingService mappingService,
        IUiDispatcher uiDispatcher)
    {
        _nyaaClient = nyaaClient;
        _animeRepo = animeRepo;
        _mappingService = mappingService;
        _uiDispatcher = uiDispatcher;
    }
}

