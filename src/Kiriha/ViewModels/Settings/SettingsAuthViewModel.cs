using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Services.Metadata;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsAuthViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly MalAuthService _authService;
    private readonly ShikiAuthService _shikiAuthService;
    private readonly ShikiHostResolver _shikiHostResolver;
    private readonly AniListAuthService _aniListAuthService;
    private readonly IAnimeRefreshService? _refreshService;
    private readonly ILocalizer _localizer;

    public SettingsAuthViewModel(
        ISettingsService settingsService,
        MalAuthService authService,
        ShikiAuthService shikiAuthService,
        ShikiHostResolver shikiHostResolver,
        AniListAuthService aniListAuthService,
        IAnimeRefreshService? refreshService = null,
        ILocalizer? localizer = null)
    {
        _settingsService = settingsService;
        _authService = authService;
        _shikiAuthService = shikiAuthService;
        _shikiHostResolver = shikiHostResolver;
        _aniListAuthService = aniListAuthService;
        _refreshService = refreshService;
        _localizer = localizer ?? LocalizationService.Active;

        var isFork = _settingsService.Current.Api.ShikiMirror == ShikiMirror.Net
                     || _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiFork) != null;
        _isShikiForkSelected = isFork;
        _isShikiOrigSelected = !isFork;
        UpdatePrimaryTrackerOptionsAvailability();
    }

    public string PrimaryTrackerName => TrackerConstants.GetDefaultDisplayName(_settingsService.Current.Api.PrimaryTrackerId ?? TrackerConstants.Ids.Mal);

    public string PrimaryTrackerBadgeText => _settingsService.Current.Api.PrimaryTrackerId switch
    {
        TrackerConstants.Ids.Mal => "MAL",
        TrackerConstants.Ids.AniList => "AL",
        _ => "SH"
    };

    public bool IsPrimaryConnected => _settingsService.Current.Api.PrimaryTrackerId switch
    {
        TrackerConstants.Ids.Mal => IsMalConnected,
        TrackerConstants.Ids.AniList => IsAniListConnected,
        TrackerConstants.Ids.ShikiOrig => IsShikiOrigConnected,
        TrackerConstants.Ids.ShikiFork => IsShikiForkConnected,
        _ => false
    };

    public bool IsMalActiveMirror => IsMalConnected && !IsMalPrimary;
    public bool IsAniListActiveMirror => IsAniListConnected && !IsAniListPrimary;
    public bool IsShikiOrigActiveMirror => IsShikiOrigConnected && !IsShikiOrigPrimary;
    public bool IsShikiForkActiveMirror => IsShikiForkConnected && !IsShikiForkPrimary;

    public bool HasActiveMirrors => IsMalActiveMirror || IsAniListActiveMirror || IsShikiActiveMirror;
    public bool HasNoActiveMirrors => !HasActiveMirrors;

    public bool IsMalConnected => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.Mal) != null;
    public bool IsShikiOrigConnected => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiOrig) != null;
    public bool IsShikiForkConnected => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiFork) != null;
    public bool IsAniListConnected => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList) != null;

    public bool IsMalPrimary => string.Equals(_settingsService.Current.Api.PrimaryTrackerId, TrackerConstants.Ids.Mal, StringComparison.OrdinalIgnoreCase);
    public bool IsShikiOrigPrimary => string.Equals(_settingsService.Current.Api.PrimaryTrackerId, TrackerConstants.Ids.ShikiOrig, StringComparison.OrdinalIgnoreCase);
    public bool IsShikiForkPrimary => string.Equals(_settingsService.Current.Api.PrimaryTrackerId, TrackerConstants.Ids.ShikiFork, StringComparison.OrdinalIgnoreCase);
    public bool IsAniListPrimary => string.Equals(_settingsService.Current.Api.PrimaryTrackerId, TrackerConstants.Ids.AniList, StringComparison.OrdinalIgnoreCase);

    public bool IsMalMirror
    {
        get => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.Mal)?.IsMirror ?? false;
        set
        {
            _settingsService.Update(s =>
            {
                var acc = s.Api.GetAccount(TrackerConstants.Ids.Mal);
                if (acc != null) acc.IsMirror = value;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
    }

    public bool IsShikiOrigMirror
    {
        get => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiOrig)?.IsMirror ?? false;
        set
        {
            _settingsService.Update(s =>
            {
                var acc = s.Api.GetAccount(TrackerConstants.Ids.ShikiOrig);
                if (acc != null) acc.IsMirror = value;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
    }

    public bool IsShikiForkMirror
    {
        get => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiFork)?.IsMirror ?? false;
        set
        {
            _settingsService.Update(s =>
            {
                var acc = s.Api.GetAccount(TrackerConstants.Ids.ShikiFork);
                if (acc != null) acc.IsMirror = value;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
    }

    public bool IsAniListMirror
    {
        get => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList)?.IsMirror ?? false;
        set
        {
            _settingsService.Update(s =>
            {
                var acc = s.Api.GetAccount(TrackerConstants.Ids.AniList);
                if (acc != null) acc.IsMirror = value;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
    }

    public string? MalUsername => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.Mal)?.Username;
    public string? ShikiOrigUsername => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiOrig)?.Username;
    public string? ShikiForkUsername => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiFork)?.Username;
    public string? AniListUsername => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList)?.Username;

    // Backward compatibility
    public bool IsLoggedIn => IsMalConnected;
    public bool IsShikiLoggedIn => IsShikiOrigConnected || IsShikiForkConnected;
    public bool IsShikiOneConnected => IsShikiOrigConnected;
    public bool IsShikiNetConnected => IsShikiForkConnected;
    public bool CanLoginShikiOne => true;
    public bool CanLoginShikiNet => true;

    [RelayCommand]
    public void SetPrimary(string trackerId)
    {
        _settingsService.Update(settings =>
        {
            settings.Api.SetPrimaryAccount(trackerId);
        }, SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        RefreshState();
    }

    [RelayCommand]
    public void SetMalPrimary() => SetPrimary(TrackerConstants.Ids.Mal);

    [RelayCommand]
    public void SetShikiOrigPrimary()
    {
        if (IsShikiForkSelected) return;
        SetPrimary(TrackerConstants.Ids.ShikiOrig);
    }

    [RelayCommand]
    public void SetShikiForkPrimary()
    {
        if (IsShikiOrigSelected) return;
        SetPrimary(TrackerConstants.Ids.ShikiFork);
    }

    [RelayCommand]
    public void SetAniListPrimary() => SetPrimary(TrackerConstants.Ids.AniList);

    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsMalConnected));
        OnPropertyChanged(nameof(IsShikiOrigConnected));
        OnPropertyChanged(nameof(IsShikiForkConnected));
        OnPropertyChanged(nameof(IsAniListConnected));

        OnPropertyChanged(nameof(IsMalPrimary));
        OnPropertyChanged(nameof(IsShikiOrigPrimary));
        OnPropertyChanged(nameof(IsShikiForkPrimary));
        OnPropertyChanged(nameof(IsAniListPrimary));

        OnPropertyChanged(nameof(IsMalMirror));
        OnPropertyChanged(nameof(IsShikiOrigMirror));
        OnPropertyChanged(nameof(IsShikiForkMirror));
        OnPropertyChanged(nameof(IsAniListMirror));

        OnPropertyChanged(nameof(MalUsername));
        OnPropertyChanged(nameof(ShikiOrigUsername));
        OnPropertyChanged(nameof(ShikiForkUsername));
        OnPropertyChanged(nameof(AniListUsername));

        OnPropertyChanged(nameof(PrimaryTrackerName));
        OnPropertyChanged(nameof(PrimaryTrackerBadgeText));
        OnPropertyChanged(nameof(SelectedPrimaryTracker));
        OnPropertyChanged(nameof(IsPrimaryConnected));

        OnPropertyChanged(nameof(IsMalActiveMirror));
        OnPropertyChanged(nameof(IsAniListActiveMirror));
        OnPropertyChanged(nameof(IsShikiOrigActiveMirror));
        OnPropertyChanged(nameof(IsShikiForkActiveMirror));
        OnPropertyChanged(nameof(HasActiveMirrors));
        OnPropertyChanged(nameof(HasNoActiveMirrors));

        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsShikiLoggedIn));
        OnPropertyChanged(nameof(IsShikiOneConnected));
        OnPropertyChanged(nameof(IsShikiNetConnected));
        OnPropertyChanged(nameof(CanLoginShikiOne));
        OnPropertyChanged(nameof(CanLoginShikiNet));

        if (IsShikiForkConnected)
        {
            _isShikiForkSelected = true;
            _isShikiOrigSelected = false;
        }
        else if (IsShikiOrigConnected)
        {
            _isShikiOrigSelected = true;
            _isShikiForkSelected = false;
        }

        NotifyShikiActiveProperties();
    }

    private static void UpsertConnectedAccount(AppSettings settings, string trackerId, OAuthTokens tokens, string? username = null)
    {
        var acc = settings.Api.GetAccount(trackerId);
        var isPrimaryTarget = string.Equals(trackerId, settings.Api.PrimaryTrackerId, StringComparison.OrdinalIgnoreCase);
        var shouldBePrimary = isPrimaryTarget || settings.Api.Accounts.All(a => !a.IsPrimary);

        if (acc == null)
        {
            acc = new TrackerAccount
            {
                TrackerId = trackerId,
                IsEnabled = true,
                IsPrimary = shouldBePrimary,
                IsMirror = !shouldBePrimary
            };
            settings.Api.Accounts.Add(acc);
        }
        else
        {
            acc.IsEnabled = true;
            if (shouldBePrimary)
            {
                acc.IsPrimary = true;
                acc.IsMirror = false;
            }
        }

        acc.Tokens = tokens;
        if (!string.IsNullOrEmpty(username)) acc.Username = username;
        if (shouldBePrimary)
        {
            settings.Api.PrimaryTrackerId = trackerId;
        }
        settings.Api.EnsurePrimaryIntegrity();
    }
}

public partial class PrimaryTrackerOption : ObservableObject
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Badge { get; init; }
    public string Domain { get; init; }

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string? _disabledReason;

    public PrimaryTrackerOption(string id, string name, string badge, string domain, bool isEnabled = true, string? disabledReason = null)
    {
        Id = id;
        Name = name;
        Badge = badge;
        Domain = domain;
        _isEnabled = isEnabled;
        _disabledReason = disabledReason;
    }
}

public enum PrimarySwitchStep
{
    None = 0,
    Confirm,
    RequiresAuth,
    Syncing,
    Success,
    Failed
}
