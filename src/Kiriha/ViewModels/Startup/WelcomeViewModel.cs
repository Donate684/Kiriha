using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Services;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.ViewModels.Startup;

public partial class WelcomeViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private bool _isLoading = true;

    private readonly AnimeListViewModel _animeListViewModel;
    private readonly ISettingsService _settingsService;

    public ISettingsService SettingsService => _settingsService;

    public WelcomeViewModel(AnimeListViewModel animeListViewModel, ISettingsService settingsService)
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
