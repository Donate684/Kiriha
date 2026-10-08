using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Kiriha.ViewModels.Settings;
using Moq;

namespace Kiriha.Tests;

public class SettingsAuthViewModelTests
{
    private readonly AppSettings _settings;
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly SettingsAuthViewModel _vm;

    public SettingsAuthViewModelTests()
    {
        _settings = new AppSettings();
        _mockSettingsService = new Mock<ISettingsService>();
        _mockSettingsService.Setup(s => s.Current).Returns(_settings);
        _mockSettingsService.Setup(s => s.Update(It.IsAny<Action<AppSettings>>(), It.IsAny<SettingsSection>(), It.IsAny<bool>()))
            .Callback<Action<AppSettings>, SettingsSection, bool>((action, _, _) => action(_settings));

        var httpClient = new HttpClient();
        var malAuth = new MalAuthService(httpClient);
        var shikiAuth = new ShikiAuthService(httpClient, _mockSettingsService.Object, new ShikiHostResolver());
        var aniListAuth = new AniListAuthService(httpClient);

        _vm = new SettingsAuthViewModel(
            _mockSettingsService.Object,
            malAuth,
            shikiAuth,
            new ShikiHostResolver(),
            aniListAuth);
    }

    [Fact]
    public void InitialState_WhenNoAccounts_AllDisconnected()
    {
        Assert.False(_vm.IsMalConnected);
        Assert.False(_vm.IsShikiOrigConnected);
        Assert.False(_vm.IsShikiForkConnected);
        Assert.False(_vm.IsAniListConnected);

        Assert.False(_vm.IsLoggedIn);
        Assert.False(_vm.IsShikiLoggedIn);
        Assert.False(_vm.IsShikiOneConnected);
        Assert.False(_vm.IsShikiNetConnected);
    }

    [Fact]
    public void InitialState_WithAccounts_ReflectsStatusAndPrimary()
    {
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            Username = "MalUser",
            IsEnabled = true,
            IsPrimary = true,
            IsMirror = false,
            Tokens = new MalTokens { AccessToken = "tok_mal" }
        });

        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.AniList,
            Username = "AniUser",
            IsEnabled = true,
            IsPrimary = false,
            IsMirror = true,
            Tokens = new AniListTokens { AccessToken = "tok_ani", UserName = "AniUser" }
        });

        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.Mal;
        _vm.RefreshState();

        Assert.True(_vm.IsMalConnected);
        Assert.True(_vm.IsMalPrimary);
        Assert.False(_vm.IsMalMirror);
        Assert.Equal("MalUser", _vm.MalUsername);

        Assert.True(_vm.IsAniListConnected);
        Assert.False(_vm.IsAniListPrimary);
        Assert.True(_vm.IsAniListMirror);
        Assert.Equal("AniUser", _vm.AniListUsername);

        Assert.True(_vm.IsLoggedIn);
        Assert.False(_vm.IsShikiLoggedIn);
    }

    [Fact]
    public void SetPrimary_SwitchesPrimaryAccount()
    {
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            IsEnabled = true,
            IsPrimary = true,
            Tokens = new MalTokens { AccessToken = "tok_mal" }
        });

        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.AniList,
            IsEnabled = true,
            IsPrimary = false,
            Tokens = new AniListTokens { AccessToken = "tok_ani" }
        });

        _settings.Api.PrimaryTrackerId = TrackerConstants.Ids.Mal;
        _vm.RefreshState();

        Assert.True(_vm.IsMalPrimary);
        Assert.False(_vm.IsAniListPrimary);

        _vm.SetPrimary(TrackerConstants.Ids.AniList);

        Assert.False(_vm.IsMalPrimary);
        Assert.True(_vm.IsAniListPrimary);
        Assert.Equal(TrackerConstants.Ids.AniList, _settings.Api.PrimaryTrackerId);
    }

    [Fact]
    public void MirrorProperty_UpdatesAccountIsMirror()
    {
        var malAccount = new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            IsEnabled = true,
            IsPrimary = false,
            IsMirror = false,
            Tokens = new MalTokens { AccessToken = "tok_mal" }
        };
        _settings.Api.Accounts.Add(malAccount);

        _vm.IsMalMirror = true;

        Assert.True(_vm.IsMalMirror);
        Assert.True(malAccount.IsMirror);

        _vm.IsMalMirror = false;

        Assert.False(_vm.IsMalMirror);
        Assert.False(malAccount.IsMirror);
    }

    [Fact]
    public void MalLogout_RemovesMalAccount()
    {
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            IsEnabled = true,
            IsPrimary = true,
            Tokens = new MalTokens { AccessToken = "tok_mal" }
        });

        Assert.True(_vm.IsMalConnected);

        _vm.MalLogout();

        Assert.False(_vm.IsMalConnected);
        Assert.Null(_settings.Api.GetAccount(TrackerConstants.Ids.Mal));
    }

    [Fact]
    public void AniListLogout_RemovesAniListAccount()
    {
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.AniList,
            IsEnabled = true,
            Tokens = new AniListTokens { AccessToken = "tok_ani" }
        });

        Assert.True(_vm.IsAniListConnected);

        _vm.AniListLogout();

        Assert.False(_vm.IsAniListConnected);
        Assert.Null(_settings.Api.GetAccount(TrackerConstants.Ids.AniList));
    }

    [Fact]
    public void ShikiOrigAndFork_IndependentState()
    {
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            IsEnabled = true,
            IsPrimary = true,
            Tokens = new ShikiTokens { AccessToken = "tok_orig", Mirror = ShikiMirror.One }
        });

        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiFork,
            IsEnabled = true,
            IsPrimary = false,
            Tokens = new ShikiTokens { AccessToken = "tok_fork", Mirror = ShikiMirror.Net }
        });

        _vm.RefreshState();

        Assert.True(_vm.IsShikiOrigConnected);
        Assert.True(_vm.IsShikiForkConnected);
        Assert.True(_vm.IsShikiLoggedIn);
        Assert.True(_vm.IsShikiOneConnected);
        Assert.True(_vm.IsShikiNetConnected);

        // Remove only fork
        _vm.ShikiForkLogout();

        Assert.True(_vm.IsShikiOrigConnected);
        Assert.False(_vm.IsShikiForkConnected);
        Assert.True(_vm.IsShikiOneConnected);
        Assert.False(_vm.IsShikiNetConnected);
        Assert.True(_vm.IsShikiLoggedIn);

        // Remove orig
        _vm.ShikiOrigLogout();

        Assert.False(_vm.IsShikiOrigConnected);
        Assert.False(_vm.IsShikiForkConnected);
        Assert.False(_vm.IsShikiLoggedIn);
    }

    [Fact]
    public void SetPrimary_WhenNoAccountsConnected_UpdatesPrimarySelectionAndProperties()
    {
        Assert.Empty(_settings.Api.Accounts);

        _vm.SetPrimary(TrackerConstants.Ids.AniList);

        Assert.True(_vm.IsAniListPrimary);
        Assert.False(_vm.IsMalPrimary);
        Assert.Equal("AniList", _vm.PrimaryTrackerName);
        Assert.NotNull(_vm.SelectedPrimaryTracker);
        Assert.Equal(TrackerConstants.Ids.AniList, _vm.SelectedPrimaryTracker.Id);
        Assert.Equal(TrackerConstants.Ids.AniList, _settings.Api.PrimaryTrackerId);
    }

    [Fact]
    public void SelectedPrimaryTracker_Setter_WhenNotConnected_OpensConfirmDialogThenRequiresAuthOnConfirm()
    {
        var shikiOrigOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.ShikiOrig);

        _vm.SelectedPrimaryTracker = shikiOrigOption;

        Assert.True(_vm.IsSwitchDialogOpen);
        Assert.True(_vm.IsSwitchConfirm);
        Assert.Equal(shikiOrigOption, _vm.PendingPrimaryTracker);
        Assert.False(_vm.IsShikiOrigPrimary);
        Assert.Equal(TrackerConstants.Ids.Mal, _settings.Api.PrimaryTrackerId);

        _vm.ConfirmPrimarySwitch();

        Assert.True(_vm.IsSwitchDialogOpen);
        Assert.True(_vm.IsSwitchRequiresAuth);
        Assert.Equal(shikiOrigOption, _vm.PendingPrimaryTracker);
    }

    [Fact]
    public void SelectedPrimaryTracker_Setter_WhenConnected_OpensConfirmDialogThenSwitchesPrimaryOnConfirm()
    {
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            IsEnabled = true,
            Tokens = new ShikiTokens()
        });

        var shikiOrigOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.ShikiOrig);

        _vm.SelectedPrimaryTracker = shikiOrigOption;

        Assert.True(_vm.IsSwitchDialogOpen);
        Assert.True(_vm.IsSwitchConfirm);
        Assert.Equal(TrackerConstants.Ids.Mal, _settings.Api.PrimaryTrackerId);

        _vm.ConfirmPrimarySwitch();

        Assert.True(_vm.IsSwitchDialogOpen);
        Assert.True(_vm.IsShikiOrigPrimary);
        Assert.False(_vm.IsMalPrimary);
        Assert.Equal(TrackerConstants.Ids.ShikiOrig, _settings.Api.PrimaryTrackerId);
    }

    [Fact]
    public void CancelPrimarySwitch_RevertsSelectionAndClosesDialog()
    {
        var shikiOrigOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.ShikiOrig);

        _vm.SelectedPrimaryTracker = shikiOrigOption;
        Assert.True(_vm.IsSwitchDialogOpen);

        _vm.CancelPrimarySwitch();

        Assert.False(_vm.IsSwitchDialogOpen);
        Assert.Equal(PrimarySwitchStep.None, _vm.SwitchStep);
        Assert.Null(_vm.PendingPrimaryTracker);
        Assert.Equal(TrackerConstants.Ids.Mal, _vm.SelectedPrimaryTracker?.Id);
    }

    [Fact]
    public void ShikimoriModeSwitching_TogglesActiveProperties()
    {
        Assert.True(_vm.IsShikiOrigSelected);
        Assert.False(_vm.IsShikiForkSelected);
        Assert.Equal("shikimori.one", _vm.ShikiActiveDomain);

        _vm.IsShikiForkSelected = true;

        Assert.False(_vm.IsShikiOrigSelected);
        Assert.True(_vm.IsShikiForkSelected);
        Assert.Equal("shikimori.rip", _vm.ShikiActiveDomain);
    }

    [Fact]
    public void ActiveMirror_AutomaticallyTrue_WhenConnectedAndNotPrimary()
    {
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            IsEnabled = true,
            IsPrimary = true,
            Tokens = new MalTokens { AccessToken = "mal_tok" }
        });
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.AniList,
            IsEnabled = true,
            IsPrimary = false,
            Tokens = new AniListTokens { AccessToken = "ani_tok" }
        });

        _vm.RefreshState();

        Assert.True(_vm.IsMalConnected);
        Assert.True(_vm.IsMalPrimary);
        Assert.False(_vm.IsMalActiveMirror);

        Assert.True(_vm.IsAniListConnected);
        Assert.False(_vm.IsAniListPrimary);
        Assert.True(_vm.IsAniListActiveMirror);
    }

    [Fact]
    public void ShikimoriModeSwitching_Blocked_WhenAccountConnected()
    {
        _settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            IsEnabled = true,
            Tokens = new ShikiTokens { AccessToken = "orig_tok" }
        });

        _vm.RefreshState();

        Assert.True(_vm.IsShikiAnyConnected);
        Assert.False(_vm.IsShikiCanSwitch);
        Assert.True(_vm.IsShikiOrigSelected);
        Assert.False(_vm.IsShikiForkSelected);

        // Attempting to switch to fork while connected must be ignored
        _vm.IsShikiForkSelected = true;

        Assert.True(_vm.IsShikiOrigSelected);
        Assert.False(_vm.IsShikiForkSelected);
        Assert.Equal("shikimori.one", _vm.ShikiActiveDomain);

        // After logging out, switching is allowed again
        _vm.ShikiOrigLogout();

        Assert.False(_vm.IsShikiAnyConnected);
        Assert.True(_vm.IsShikiCanSwitch);

        _vm.IsShikiForkSelected = true;
        Assert.True(_vm.IsShikiForkSelected);
        Assert.False(_vm.IsShikiOrigSelected);
        Assert.Equal("shikimori.rip", _vm.ShikiActiveDomain);
    }

    [Fact]
    public void PrimaryTrackerOptions_WhenShikiOrigActive_ShikiForkIsDisabled()
    {
        var shikiOrigOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.ShikiOrig);
        var shikiForkOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.ShikiFork);
        var malOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.Mal);
        var aniListOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.AniList);

        Assert.True(_vm.IsShikiOrigSelected);
        Assert.True(malOption.IsEnabled);
        Assert.True(aniListOption.IsEnabled);
        Assert.True(shikiOrigOption.IsEnabled);
        Assert.False(shikiForkOption.IsEnabled);
        Assert.NotNull(shikiForkOption.DisabledReason);

        // Selecting disabled fork option should be ignored
        _vm.SelectedPrimaryTracker = shikiForkOption;
        Assert.False(_vm.IsSwitchDialogOpen);
        Assert.Null(_vm.PendingPrimaryTracker);

        // Calling SetShikiForkPrimary directly should be ignored
        _vm.SetShikiForkPrimary();
        Assert.False(_vm.IsShikiForkPrimary);
    }

    [Fact]
    public void PrimaryTrackerOptions_WhenShikiForkActive_ShikiOrigIsDisabled()
    {
        _vm.IsShikiForkSelected = true;

        var shikiOrigOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.ShikiOrig);
        var shikiForkOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.ShikiFork);
        var malOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.Mal);
        var aniListOption = _vm.AvailablePrimaryTrackers.First(t => t.Id == TrackerConstants.Ids.AniList);

        Assert.True(_vm.IsShikiForkSelected);
        Assert.True(malOption.IsEnabled);
        Assert.True(aniListOption.IsEnabled);
        Assert.True(shikiForkOption.IsEnabled);
        Assert.False(shikiOrigOption.IsEnabled);
        Assert.NotNull(shikiOrigOption.DisabledReason);

        // Selecting disabled orig option should be ignored
        _vm.SelectedPrimaryTracker = shikiOrigOption;
        Assert.False(_vm.IsSwitchDialogOpen);
        Assert.Null(_vm.PendingPrimaryTracker);

        // Calling SetShikiOrigPrimary directly should be ignored
        _vm.SetShikiOrigPrimary();
        Assert.False(_vm.IsShikiOrigPrimary);

        // Selecting enabled fork option opens dialog
        _vm.SelectedPrimaryTracker = shikiForkOption;
        Assert.True(_vm.IsSwitchDialogOpen);
        Assert.Equal(shikiForkOption, _vm.PendingPrimaryTracker);
    }

    [Fact]
    public void AuthDialog_InitialState_IsClosed()
    {
        Assert.False(_vm.IsAuthDialogOpen);
        Assert.Null(_vm.ActiveAuthTrackerId);
        Assert.Null(_vm.ActiveAuthTrackerName);
        Assert.False(_vm.IsAuthFailed);
    }

    [Fact]
    public async Task ExecuteAuthFlowAsync_SetsActiveStateAndClosesOnSuccess()
    {
        var executed = false;
        var result = await _vm.ExecuteAuthFlowAsync(
            TrackerConstants.Ids.Mal,
            "MyAnimeList",
            "MAL",
            TrackerConstants.Domains.Mal,
            "#2e51a2",
            async ct =>
            {
                executed = true;
                Assert.True(_vm.IsAuthDialogOpen);
                Assert.Equal(TrackerConstants.Ids.Mal, _vm.ActiveAuthTrackerId);
                Assert.Equal("MyAnimeList", _vm.ActiveAuthTrackerName);
                Assert.Equal("MAL", _vm.ActiveAuthTrackerBadge);
                Assert.Equal("#2e51a2", _vm.ActiveAuthBadgeBackground);
                await Task.Yield();
                return true;
            });

        Assert.True(executed);
        Assert.True(result);
        Assert.False(_vm.IsAuthDialogOpen);
        Assert.False(_vm.IsAuthFailed);
    }

    [Fact]
    public async Task ExecuteAuthFlowAsync_WhenCancelled_ClosesDialogAndResets()
    {
        var tcs = new TaskCompletionSource<bool>();
        var flowTask = _vm.ExecuteAuthFlowAsync(
            TrackerConstants.Ids.AniList,
            "AniList",
            "AL",
            TrackerConstants.Domains.AniList,
            "#02a9ff",
            async ct =>
            {
                using var reg = ct.Register(() => tcs.TrySetCanceled());
                await tcs.Task;
                return true;
            });

        Assert.True(_vm.IsAuthDialogOpen);
        _vm.CancelAuth();

        var result = await flowTask;
        Assert.False(result);
        Assert.False(_vm.IsAuthDialogOpen);
    }

    [Fact]
    public async Task ExecuteAuthFlowAsync_WhenAlreadyOpen_BlocksSecondInvocation()
    {
        var tcs = new TaskCompletionSource<bool>();
        var firstFlow = _vm.ExecuteAuthFlowAsync(
            TrackerConstants.Ids.Mal,
            "MyAnimeList",
            "MAL",
            TrackerConstants.Domains.Mal,
            "#2e51a2",
            async ct =>
            {
                await tcs.Task;
                return true;
            });

        Assert.True(_vm.IsAuthDialogOpen);

        // Attempting a second concurrent auth should immediately return false without doing anything
        var secondResult = await _vm.ExecuteAuthFlowAsync(
            TrackerConstants.Ids.AniList,
            "AniList",
            "AL",
            TrackerConstants.Domains.AniList,
            "#02a9ff",
            async ct =>
            {
                await Task.Yield();
                return true;
            });

        Assert.False(secondResult);
        Assert.Equal(TrackerConstants.Ids.Mal, _vm.ActiveAuthTrackerId);

        // Finish first
        tcs.SetResult(true);
        var firstResult = await firstFlow;
        Assert.True(firstResult);
        Assert.False(_vm.IsAuthDialogOpen);
    }
}



