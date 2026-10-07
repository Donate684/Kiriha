using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Metadata;
using Kiriha.Utils.Collections;

namespace Kiriha.ViewModels.Search;

public partial class SearchViewModel : ViewModelBase, IDisposable
{
    private readonly IMalApiService _apiService;
    private readonly IShikiMetadataService _shikiMetadataService;
    private readonly LoadQueueService _queueService;
    private readonly IAnimeListActionService _listActionService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizer _localizer;

    public IDialogService DialogService => _dialogService;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hideInLists;
    [NotifyPropertyChangedFor(nameof(DisplayAdultFilter))]
    [ObservableProperty] private AdultFilterMode _adultFilter = AdultFilterMode.Hide;

    public AdultFilterMode[] AdultFilterOptions { get; } = [AdultFilterMode.Hide, AdultFilterMode.Include, AdultFilterMode.Only];
    public string DisplayAdultFilter => AdultFilter switch
    {
        AdultFilterMode.Hide => _localizer.GetLoc("filters.adult.hide"),
        AdultFilterMode.Include => _localizer.GetLoc("filters.adult.include"),
        AdultFilterMode.Only => _localizer.GetLoc("filters.adult.only"),
        _ => "18+"
    };

    [RelayCommand]
    public void CycleAdultFilter()
    {
        AdultFilter = AdultFilter switch
        {
            AdultFilterMode.Hide => AdultFilterMode.Include,
            AdultFilterMode.Include => AdultFilterMode.Only,
            AdultFilterMode.Only => AdultFilterMode.Hide,
            _ => AdultFilterMode.Hide
        };
    }

    public BulkObservableCollection<AnimeEntity> SearchResults { get; } = new();

    private CancellationTokenSource? _searchCts;
    private bool _isDisposed;
    private readonly Kiriha.Utils.Async.Debouncer _searchDebouncer;

    public SearchViewModel(IMalApiService apiService, IShikiMetadataService shikiMetadataService,
        LoadQueueService queueService,
        IAnimeListActionService listActionService, IDialogService dialogService,
        ILocalizer localizer)
    {
        _apiService = apiService;
        _shikiMetadataService = shikiMetadataService;
        _queueService = queueService;
        _listActionService = listActionService;
        _dialogService = dialogService;
        _localizer = localizer;

        _searchDebouncer = new Kiriha.Utils.Async.Debouncer(TimeSpan.FromMilliseconds(800), _ =>
        {
            return Dispatcher.UIThread.InvokeAsync(() => PerformSearch());
        });
    }

    /// <summary>
    /// Called from view's ElementPrepared handler to lazily load images
    /// only for items that have entered the viewport.
    /// </summary>
    public void EnqueueItemForViewport(AnimeEntity item)
    {
        if (item is null) return;
        _queueService.EnqueueForViewport([item]);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        var cts = Interlocked.Exchange(ref _searchCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        _searchDebouncer?.Dispose();
    }
}
