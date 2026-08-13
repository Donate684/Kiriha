using Kiriha.Services.Data.Settings;
using Kiriha.ViewModels.Settings;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Navigation;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;

namespace Kiriha.ViewModels.Main;

public partial class MainWindowViewModel : ViewModelBase, IRecipient<NavigationMessage>, IDisposable
{
    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private int _selectedNavigationIndex = 0;

    [ObservableProperty]
    private bool _isSettingsSelected = false;

    [ObservableProperty]
    private bool _isNavigationBlocked = false;

    [ObservableProperty]
    private ViewModelBase _currentPage = null!;

    public SettingsViewModel? SettingsViewModel => EnsureSettingsViewModel();

    // IViewModelFactory delivers a fresh transient instance on each navigation —
    // see DI registrations: WelcomeViewModel and SearchViewModel are AddTransient.
    private readonly IViewModelFactory _viewModelFactory;

    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;

    public MainWindowViewModel(
        IViewModelFactory viewModelFactory,
        Kiriha.Core.Abstractions.Services.ISettingsService settingsService)
    {
        _viewModelFactory = viewModelFactory;
        _settingsService = settingsService;

        // Load saved sidebar state
        IsPaneOpen = _settingsService.Current.UI.IsPaneOpen;

        // Register for navigation messages
        WeakReferenceMessenger.Default.Register(this);

        // Start on Welcome page
        NavigateWelcome();
    }

    partial void OnIsPaneOpenChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.IsPaneOpen = value, Kiriha.Core.Abstractions.Services.SettingsSection.UI);
    }

    [RelayCommand]
    public void TriggerPane()
    {
        if (IsNavigationBlocked) return;
        IsPaneOpen = !IsPaneOpen;
    }

    [RelayCommand]
    public void TestPlayer()
    {
        Kiriha.Utils.PlayerProcessHelper.LaunchPlayer();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (CurrentPage is IDisposable disposable && !_cachedVms.Contains(CurrentPage))
            {
                disposable.Dispose();
            }

            (UpdateDialog as IDisposable)?.Dispose();

            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
