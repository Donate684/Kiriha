using Kiriha.Core.Tracking.Api;
using Kiriha.Services.Data.Settings;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Services;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Services.Data;
using Serilog;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsAuthViewModel : ObservableObject
{
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly MalAuthService _authService;
    private readonly ShikiAuthService _shikiAuthService;
    private readonly ShikiHostResolver _shikiHostResolver;

    public SettingsAuthViewModel(
        Kiriha.Core.Abstractions.Services.ISettingsService settingsService,
        MalAuthService authService,
        ShikiAuthService shikiAuthService,
        ShikiHostResolver shikiHostResolver)
    {
        _settingsService = settingsService;
        _authService = authService;
        _shikiAuthService = shikiAuthService;
        _shikiHostResolver = shikiHostResolver;

        IsLoggedIn = _settingsService.Current.Api.Mal != null;
        IsShikiLoggedIn = _settingsService.Current.Api.Shiki != null;
    }

    public bool IsShikiOneConnected => _settingsService.Current.Api.Shiki?.Mirror == ShikiMirror.One;
    public bool IsShikiNetConnected => _settingsService.Current.Api.Shiki?.Mirror == ShikiMirror.Net;

    public bool CanLoginShikiOne => IsLoggedIn && !IsShikiNetConnected;
    public bool CanLoginShikiNet => IsLoggedIn && !IsShikiOneConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiOne))]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiNet))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginOneCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginNetCommand))]
    private bool _isLoggedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiOne))]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiNet))]
    [NotifyPropertyChangedFor(nameof(IsShikiOneConnected))]
    [NotifyPropertyChangedFor(nameof(IsShikiNetConnected))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginOneCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginNetCommand))]
    private bool _isShikiLoggedIn;

    [RelayCommand]
    public async Task MalLogin()
    {
        var tokens = await _authService.LoginAsync();
        if (tokens != null)
        {
            _settingsService.Update(settings => settings.Api.Mal = tokens, Kiriha.Core.Abstractions.Services.SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            IsLoggedIn = true;
            ShikiLoginOneCommand.NotifyCanExecuteChanged();
            ShikiLoginNetCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public void MalLogout()
    {
        _settingsService.Update(settings =>
        {
            settings.Api.Mal = null;
            settings.Api.Shiki = null;
        }, Kiriha.Core.Abstractions.Services.SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        IsLoggedIn = false;
        IsShikiLoggedIn = false;
        ShikiLoginOneCommand.NotifyCanExecuteChanged();
        ShikiLoginNetCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoginShikiOne))]
    public Task ShikiLoginOne() => LoginToMirrorAsync(ShikiMirror.One);

    [RelayCommand(CanExecute = nameof(CanLoginShikiNet))]
    public Task ShikiLoginNet() => LoginToMirrorAsync(ShikiMirror.Net);

    private async Task LoginToMirrorAsync(ShikiMirror mirror)
    {
        var current = _settingsService.Read(settings => settings.Api.Shiki);
        if (current != null && current.Mirror != mirror)
        {
            Log.Warning("Refused to log into Shikimori {Requested}: already connected to {Active}.", mirror, current.Mirror);
            return;
        }

        _settingsService.Update(settings => settings.Api.ShikiMirror = mirror, Kiriha.Core.Abstractions.Services.SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        _shikiHostResolver.Reset();

        var tokens = await _shikiAuthService.LoginAsync();
        if (tokens != null)
        {
            tokens.Mirror = mirror;
            _settingsService.Update(settings => settings.Api.Shiki = tokens, Kiriha.Core.Abstractions.Services.SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            IsShikiLoggedIn = true;
        }
    }

    [RelayCommand]
    public void ShikiLogout()
    {
        _settingsService.Update(settings => settings.Api.Shiki = null, Kiriha.Core.Abstractions.Services.SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        IsShikiLoggedIn = false;
    }
}
