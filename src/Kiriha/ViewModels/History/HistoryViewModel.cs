using Kiriha.Services.Data.Core;
using Kiriha.Core.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Dialogs;
using Kiriha.Models;
using Kiriha.Core.Tracking.Api;
using Kiriha.Services.Data;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.History;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly HistoryService _historyService;
    private readonly DatabaseInitializer _dbInit;
    private readonly Kiriha.Core.Repositories.IAnimeRepository _animeRepo;
    private readonly Kiriha.Core.Services.IMalApiService _malApi;
    private readonly IDialogService _dialogs;
    private readonly Kiriha.Core.Services.ISettingsService _settings;
    private List<HistoryItem> _rawItems = new();

    [ObservableProperty]
    private ObservableCollection<HistoryGroup> _groupedHistory = new();

    [ObservableProperty]
    private bool _hasHistory;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>0=All, 1=Today, 2=Week, 3=Month</summary>
    [ObservableProperty]
    private int _selectedPeriod;

    /// <summary>0=All, or one of HistoryItem.ActionType values.</summary>
    [ObservableProperty]
    private int _selectedAction;

    public HistoryViewModel(
        HistoryService historyService,
        DatabaseInitializer dbInit,
        Kiriha.Core.Repositories.IAnimeRepository animeRepo,
        Kiriha.Core.Services.IMalApiService malApi,
        IDialogService dialogs,
        Kiriha.Core.Services.ISettingsService settings)
    {
        _historyService = historyService;
        _dbInit = dbInit;
        _animeRepo = animeRepo;
        _malApi = malApi;
        _dialogs = dialogs;
        _settings = settings;
        RefreshHistory().SafeFireAndForget("HistoryInit");
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilters();
    partial void OnSelectedPeriodChanged(int value)
    {
        ApplyFilters();
        NotifyPeriodFlags();
    }
    partial void OnSelectedActionChanged(int value)
    {
        ApplyFilters();
        NotifyActionFlags();
    }

    // â”€â”€â”€ Radio-button friendly flags (also safe for ToggleButton: can't uncheck) â”€â”€â”€
    public bool IsPeriodAll { get => SelectedPeriod == 0; set { if (value) SelectedPeriod = 0; else OnPropertyChanged(nameof(IsPeriodAll)); } }
    public bool IsPeriodToday { get => SelectedPeriod == 1; set { if (value) SelectedPeriod = 1; else OnPropertyChanged(nameof(IsPeriodToday)); } }
    public bool IsPeriodWeek { get => SelectedPeriod == 2; set { if (value) SelectedPeriod = 2; else OnPropertyChanged(nameof(IsPeriodWeek)); } }
    public bool IsPeriodMonth { get => SelectedPeriod == 3; set { if (value) SelectedPeriod = 3; else OnPropertyChanged(nameof(IsPeriodMonth)); } }

    public bool IsActionAll { get => SelectedAction == 0; set { if (value) SelectedAction = 0; } }
    public bool IsActionWatched { get => SelectedAction == 1; set { if (value) SelectedAction = 1; } }
    public bool IsActionCompleted { get => SelectedAction == 6; set { if (value) SelectedAction = 6; } }
    public bool IsActionDropped { get => SelectedAction == 7; set { if (value) SelectedAction = 7; } }
    public bool IsActionScoreSet { get => SelectedAction == 5; set { if (value) SelectedAction = 5; } }

    private void NotifyPeriodFlags()
    {
        OnPropertyChanged(nameof(IsPeriodAll));
        OnPropertyChanged(nameof(IsPeriodToday));
        OnPropertyChanged(nameof(IsPeriodWeek));
        OnPropertyChanged(nameof(IsPeriodMonth));
    }

    private void NotifyActionFlags()
    {
        OnPropertyChanged(nameof(IsActionAll));
        OnPropertyChanged(nameof(IsActionWatched));
        OnPropertyChanged(nameof(IsActionCompleted));
        OnPropertyChanged(nameof(IsActionDropped));
        OnPropertyChanged(nameof(IsActionScoreSet));
    }
}
