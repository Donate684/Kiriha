using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsAuthViewModel
{
    public bool IsShikiAnyConnected => IsShikiOrigConnected || IsShikiForkConnected;
    public bool IsShikiCanSwitch => !IsShikiAnyConnected;

    private bool _isShikiForkSelected;
    public bool IsShikiForkSelected
    {
        get => _isShikiForkSelected;
        set
        {
            if (value && !_isShikiForkSelected)
            {
                if (IsShikiAnyConnected) return;
                _isShikiForkSelected = true;
                _isShikiOrigSelected = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsShikiOrigSelected));
                _settingsService.Update(s => s.Api.ShikiMirror = ShikiMirror.Net, SettingsSection.Api, save: false);
                _settingsService.SaveImmediate();
                NotifyShikiActiveProperties();
            }
        }
    }

    private bool _isShikiOrigSelected = true;
    public bool IsShikiOrigSelected
    {
        get => _isShikiOrigSelected;
        set
        {
            if (value && !_isShikiOrigSelected)
            {
                if (IsShikiAnyConnected) return;
                _isShikiOrigSelected = true;
                _isShikiForkSelected = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsShikiForkSelected));
                _settingsService.Update(s => s.Api.ShikiMirror = ShikiMirror.One, SettingsSection.Api, save: false);
                _settingsService.SaveImmediate();
                NotifyShikiActiveProperties();
            }
        }
    }

    public bool IsShikiActiveConnected => IsShikiForkSelected ? IsShikiForkConnected : IsShikiOrigConnected;
    public bool IsShikiActivePrimary => IsShikiForkSelected ? IsShikiForkPrimary : IsShikiOrigPrimary;
    public bool IsShikiActiveMirror => IsShikiActiveConnected && !IsShikiActivePrimary;
    public string ShikiActiveDomain => IsShikiForkSelected ? TrackerConstants.Domains.ShikiFork : TrackerConstants.Domains.ShikiOrig;
    public string ShikiActiveDescription => IsShikiForkSelected
        ? _localizer.GetLoc("settings.accounts_hub.shikimori_fork_sub")
        : _localizer.GetLoc("settings.accounts_hub.shikimori_orig_sub");
    public string? ShikiActiveUsername => IsShikiForkSelected ? ShikiForkUsername : ShikiOrigUsername;
    public IRelayCommand ShikiActiveLoginCommand => IsShikiForkSelected ? ShikiForkLoginCommand : ShikiOrigLoginCommand;
    public IRelayCommand ShikiActiveLogoutCommand => IsShikiForkSelected ? ShikiForkLogoutCommand : ShikiOrigLogoutCommand;

    public void NotifyShikiActiveProperties()
    {
        UpdatePrimaryTrackerOptionsAvailability();

        OnPropertyChanged(nameof(IsShikiAnyConnected));
        OnPropertyChanged(nameof(IsShikiCanSwitch));
        OnPropertyChanged(nameof(IsShikiOrigSelected));
        OnPropertyChanged(nameof(IsShikiForkSelected));
        OnPropertyChanged(nameof(IsShikiActiveConnected));
        OnPropertyChanged(nameof(IsShikiActivePrimary));
        OnPropertyChanged(nameof(IsShikiActiveMirror));
        OnPropertyChanged(nameof(ShikiActiveDomain));
        OnPropertyChanged(nameof(ShikiActiveDescription));
        OnPropertyChanged(nameof(ShikiActiveUsername));
        OnPropertyChanged(nameof(ShikiActiveLoginCommand));
        OnPropertyChanged(nameof(ShikiActiveLogoutCommand));
        OnPropertyChanged(nameof(HasActiveMirrors));
        OnPropertyChanged(nameof(HasNoActiveMirrors));
        OnPropertyChanged(nameof(IsPrimaryConnected));
    }
}
