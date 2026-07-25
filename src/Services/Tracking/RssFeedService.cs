using Kiriha.Core.Infrastructure;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;

namespace Kiriha.Services.Tracking;

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

