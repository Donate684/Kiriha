using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

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

