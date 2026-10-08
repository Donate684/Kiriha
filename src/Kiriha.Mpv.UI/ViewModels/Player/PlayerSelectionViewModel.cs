using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerSelectionViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IExternalMediaDetector _anisthesia;
    private readonly List<PlayerSelectionItem> _allPlayers = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _enabledCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _selectionSummary = string.Empty;
    [ObservableProperty] private bool _hasNoMatches;

    public ObservableCollection<PlayerSelectionItem> ActivePlayers { get; } = new();
    public ObservableCollection<PlayerSelectionItem> VideoPlayers { get; } = new();

    public PlayerSelectionViewModel(IExternalMediaDetector anisthesia, ISettingsService settingsService)
    {
        _anisthesia = anisthesia;
        _settingsService = settingsService;
        var allowed = _settingsService.Current.System.Scrobbler.AllowedProcesses;
        bool listWasEmpty = allowed.Count == 0;
        var running = _anisthesia.RunningPlayerNames;

        foreach (var p in _anisthesia.AvailablePlayers.Where(x => x.Type != PlayerType.WebBrowser).OrderBy(x => x.Name))
        {
            // If the list is empty, default behavior permits all video players.
            bool isEnabled = listWasEmpty || allowed.Contains(p.Name);
            bool isRunning = running.Contains(p.Name);

            var item = new PlayerSelectionItem(p.Name, p.Type, isEnabled, isRunning);
            item.PropertyChanged += OnItemPropertyChanged;
            _allPlayers.Add(item);
        }

        _anisthesia.RunningPlayersChanged += OnRunningPlayersChanged;
        RefreshLists();
        UpdateCounts();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerSelectionItem.IsEnabled))
        {
            UpdateCounts();
        }
    }

    private void UpdateCounts()
    {
        EnabledCount = _allPlayers.Count(p => p.IsEnabled);
        TotalCount = _allPlayers.Count;
        SelectionSummary = $"{EnabledCount} / {TotalCount}";
    }

    private void OnRunningPlayersChanged(object? sender, IReadOnlySet<string> running)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var p in _allPlayers)
            {
                p.IsRunning = running.Contains(p.Name);
            }
            RefreshLists();
        });
    }

    partial void OnSearchTextChanged(string value) => RefreshLists();

    private void RefreshLists()
    {
        ActivePlayers.Clear();
        VideoPlayers.Clear();

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allPlayers
            : _allPlayers.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var p in filtered)
        {
            if (p.IsRunning) ActivePlayers.Add(p);
            else VideoPlayers.Add(p);
        }

        HasNoMatches = ActivePlayers.Count == 0 && VideoPlayers.Count == 0;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var p in _allPlayers) p.IsEnabled = true;
        UpdateCounts();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var p in _allPlayers) p.IsEnabled = false;
        UpdateCounts();
    }

    [RelayCommand]
    private void SaveAndClose(Avalonia.Controls.Window? window)
    {
        var enabled = _allPlayers.Where(p => p.IsEnabled).Select(p => p.Name).ToList();
        _settingsService.Update(settings =>
        {
            settings.System.Scrobbler.AllowedProcesses.Clear();
            foreach (var name in enabled) settings.System.Scrobbler.AllowedProcesses.Add(name);
        }, SettingsSection.System, save: false);
        _settingsService.SaveImmediate();
        window?.Close();
    }

    [RelayCommand]
    private void Cancel(Avalonia.Controls.Window? window)
    {
        window?.Close();
    }

    public void Dispose()
    {
        _anisthesia.RunningPlayersChanged -= OnRunningPlayersChanged;
        foreach (var item in _allPlayers)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }
    }
}

public partial class PlayerSelectionItem : ObservableObject
{
    public string Name { get; }
    public PlayerType Type { get; }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isRunning;

    public PlayerSelectionItem(string name, PlayerType type, bool isEnabled, bool isRunning = false)
    {
        Name = name;
        Type = type;
        _isEnabled = isEnabled;
        _isRunning = isRunning;
    }

    [RelayCommand]
    private void Toggle()
    {
        IsEnabled = !IsEnabled;
    }
}






