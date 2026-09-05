using Kiriha.Services.Data.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.ViewModels.Analytics;

public partial class AnalyticsViewModel : ViewModelBase
{
    private readonly IAnimeRepository _animeRepo;
    private readonly HistoryService _historyService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

                    public OverviewSectionViewModel Overview { get; }
    public TastesSectionViewModel Tastes { get; }
    public WatchNextSectionViewModel WatchNext { get; }
    public ReadNextSectionViewModel ReadNext { get; }
    public HistorySectionViewModel History { get; }

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _updatedAt = string.Empty;
    [ObservableProperty] private int _selectedSection;

    public bool IsOverviewSelected
    {
        get => SelectedSection == 0;
        set { if (value) SelectedSection = 0; }
    }

    public bool IsRatingsSelected
    {
        get => SelectedSection == 1;
        set { if (value) SelectedSection = 1; }
    }

    public bool IsTasteSelected
    {
        get => SelectedSection == 2;
        set { if (value) SelectedSection = 2; }
    }

    public bool IsWatchNextSelected
    {
        get => SelectedSection == 3;
        set { if (value) SelectedSection = 3; }
    }

    public bool IsReadNextSelected
    {
        get => SelectedSection == 4;
        set { if (value) SelectedSection = 4; }
    }

    public bool IsHistorySelected
    {
        get => SelectedSection == 5;
        set { if (value) SelectedSection = 5; }
    }

    public AnalyticsViewModel(IAnimeRepository animeRepo, HistoryService historyService, ILocalizer localizer)
    {
        _animeRepo = animeRepo;
        _historyService = historyService;
        Overview = new(localizer);
        Tastes = new(localizer);
        WatchNext = new(localizer);
        ReadNext = new(localizer);
        History = new();
    }

    partial void OnSelectedSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewSelected));
        OnPropertyChanged(nameof(IsRatingsSelected));
        OnPropertyChanged(nameof(IsTasteSelected));
        OnPropertyChanged(nameof(IsWatchNextSelected));
        OnPropertyChanged(nameof(IsReadNextSelected));
        OnPropertyChanged(nameof(IsHistorySelected));
    }

    [RelayCommand]
    public async Task Refresh()
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsRefreshing = true;

            var items = _animeRepo.Collection.ToList();
            var history = await _historyService.GetHistoryAsync(5000);
            
            HasData = items.Count > 0;
            UpdatedAt = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);

            if (!HasData)
            {
                return;
            }

            var nonPlanned = items.Where(x => x.Status != UserAnimeStatus.PlanToWatch && x.Status != UserAnimeStatus.None && x.Status != UserAnimeStatus.Dropped).ToList();
            var completed = items.Where(x => x.Status == UserAnimeStatus.Completed).ToList();
            var scored = nonPlanned
                .Select(x => int.TryParse(x.Score, out var score) ? score : 0)
                .Where(x => x > 0)
                .ToList();

            Overview.Refresh(items, nonPlanned, completed, scored);
            Tastes.Refresh(items, nonPlanned);
            
            var animes = items.Where(x => x.MediaKind == MediaKind.Anime).ToList();
            var mangas = items.Where(x => x.MediaKind != MediaKind.Anime).ToList();
            
            WatchNext.Refresh(animes);
            ReadNext.Refresh(mangas);
            History.Refresh(history, items, completed);
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }
}

