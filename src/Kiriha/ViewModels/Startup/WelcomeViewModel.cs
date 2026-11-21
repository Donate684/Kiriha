using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Services.AppLifecycle;

namespace Kiriha.ViewModels.Startup;

public partial class WelcomeViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private bool _isLoading = true;

    private readonly AppReadinessService _readiness;
    private readonly IUiDispatcher _dispatcher;
    private readonly ISettingsService _settingsService;

    public ISettingsService SettingsService => _settingsService;

    public WelcomeViewModel(AppReadinessService readiness, ISettingsService settingsService, IUiDispatcher dispatcher)
    {
        _readiness = readiness;
        _settingsService = settingsService;
        _dispatcher = dispatcher;
        _readiness.StateChanged += OnReadinessChanged;
        _isLoading = IsStarting(readiness.State);
    }

    private void OnReadinessChanged(object? sender, AppReadinessState state) =>
        _dispatcher.Post(() => IsLoading = IsStarting(_readiness.State));

    private static bool IsStarting(AppReadinessState state) =>
        state is AppReadinessState.NotStarted or AppReadinessState.Starting;

    public void Dispose()
    {
        _readiness.StateChanged -= OnReadinessChanged;
    }
}
