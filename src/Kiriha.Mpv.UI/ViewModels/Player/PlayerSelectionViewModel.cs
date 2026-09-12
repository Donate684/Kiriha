using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Tracking;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Infrastructure.Tracking.Integration;
using Kiriha.Services.Data;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerSelectionViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IExternalMediaDetector _anisthesia;
    private readonly List<PlayerSelectionItem> _allPlayers = new();

    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<PlayerSelectionItem> ActivePlayers { get; } = new();
    public ObservableCollection<PlayerSelectionItem> VideoPlayers { get; } = new();
    public ObservableCollection<PlayerSelectionItem> WebBrowsers { get; } = new();

    public PlayerSelectionViewModel(IExternalMediaDetector anisthesia, ISettingsService settingsService)
    {
        _anisthesia = anisthesia;
        _settingsService = settingsService;
        var allowed = _settingsService.Current.System.Scrobbler.AllowedProcesses;
        bool listWasEmpty = allowed.Count == 0;
        var running = _anisthesia.RunningPlayerNames;

        foreach (var p in _anisthesia.AvailablePlayers.OrderBy(x => x.Name))
        {
            // If the list is empty, default behavior permits all video players (except browsers).
            bool isEnabled = listWasEmpty ? (p.Type != PlayerType.WebBrowser) : allowed.Contains(p.Name);
            bool isRunning = running.Contains(p.Name);

            var item = new PlayerSelectionItem(p.Name, p.Type, isEnabled, isRunning);
            _allPlayers.Add(item);
        }

        _anisthesia.RunningPlayersChanged += OnRunningPlayersChanged;
        RefreshLists();
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
        WebBrowsers.Clear();

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allPlayers
            : _allPlayers.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var p in filtered)
        {
            if (p.IsRunning) ActivePlayers.Add(p);
            else if (p.Type == PlayerType.WebBrowser) WebBrowsers.Add(p);
            else VideoPlayers.Add(p);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var p in _allPlayers) p.IsEnabled = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var p in _allPlayers) p.IsEnabled = false;
    }

    [RelayCommand]
    private void SaveAndClose(Avalonia.Controls.Window window)
    {
        var enabled = _allPlayers.Where(p => p.IsEnabled).Select(p => p.Name).ToList();
        _settingsService.Update(settings =>
        {
            settings.System.Scrobbler.AllowedProcesses.Clear();
            foreach (var name in enabled) settings.System.Scrobbler.AllowedProcesses.Add(name);
        }, SettingsSection.System, save: false);
        _settingsService.SaveImmediate();
        window.Close();
    }

    public void Dispose()
    {
        _anisthesia.RunningPlayersChanged -= OnRunningPlayersChanged;
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
    }
}






