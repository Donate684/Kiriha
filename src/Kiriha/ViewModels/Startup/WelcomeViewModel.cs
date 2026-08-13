using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Services.Data.Settings;
using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Services.Data;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.ViewModels.Startup;

public partial class WelcomeViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private bool _isLoading = true;

    private readonly AnimeListViewModel _animeListViewModel;
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;

    public Kiriha.Core.Abstractions.Services.ISettingsService SettingsService => _settingsService;

    public WelcomeViewModel(AnimeListViewModel animeListViewModel, Kiriha.Core.Abstractions.Services.ISettingsService settingsService)
    {
        _animeListViewModel = animeListViewModel;
        _settingsService = settingsService;
        _isLoading = _animeListViewModel.IsBusy;
        _animeListViewModel.PropertyChanged += OnAnimeListPropertyChanged;
    }

    private void OnAnimeListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnimeListViewModel.IsBusy))
        {
            IsLoading = _animeListViewModel.IsBusy;
        }
    }

    public void Dispose()
    {
        if (_animeListViewModel != null)
        {
            _animeListViewModel.PropertyChanged -= OnAnimeListPropertyChanged;
        }
    }
}
