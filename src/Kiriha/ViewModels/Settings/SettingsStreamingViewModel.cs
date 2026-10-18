using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Helpers;
using Kiriha.Core.Streaming.Services;
using Kiriha.Views.Settings;
using Serilog;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsStreamingViewModel : ObservableObject
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly ISettingsService _settingsService;
    private readonly Anime365HostResolver? _hostResolver;

    public IReadOnlyList<Anime365MirrorOption> Anime365Mirrors { get; } =
    [
        new("", UIUtils.GetLoc("settings.streaming.anime365_mirror_auto")),
        new(StreamingConstants.Anime365.MirrorApp, UIUtils.GetLoc("settings.streaming.anime365_mirror_app")),
        new(StreamingConstants.Anime365.MirrorCo, UIUtils.GetLoc("settings.streaming.anime365_mirror_co")),
        new(StreamingConstants.Anime365.MirrorSmotretAnimeCo, UIUtils.GetLoc("settings.streaming.anime365_mirror_smotretanime_co")),
        new(StreamingConstants.Anime365.MirrorCom, UIUtils.GetLoc("settings.streaming.anime365_mirror_com")),
        new(StreamingConstants.Anime365.MirrorOrg, UIUtils.GetLoc("settings.streaming.anime365_mirror_org")),
    ];

    [ObservableProperty]
    private Anime365MirrorOption _selectedAnime365Mirror;

    [ObservableProperty]
    private bool _isAnimeLibLoggedIn;

    [ObservableProperty]
    private string _animeLibUsername = string.Empty;

    [ObservableProperty]
    private bool _preferDirectMp4;

    [ObservableProperty]
    private bool _enableAniLibria = true;

    [ObservableProperty]
    private bool _enableAnimeLib;

    [ObservableProperty]
    private bool _enableDreamCast;

    [ObservableProperty]
    private bool _enableYummyAnime;

    [ObservableProperty]
    private bool _enableAnime365;

    [ObservableProperty]
    private bool _isAnime365LoggedIn;

    [ObservableProperty]
    private string _anime365Username = string.Empty;

    [ObservableProperty]
    private string _anime365StatusText = string.Empty;

    [ObservableProperty]
    private string _anime365Email = string.Empty;

    [ObservableProperty]
    private string _anime365Password = string.Empty;

    [ObservableProperty]
    private string _anime365ManualToken = string.Empty;

    [ObservableProperty]
    private bool _isAnime365ManualTokenExpanded;

    [ObservableProperty]
    private bool _isAnime365LoggingIn;

    [ObservableProperty]
    private bool _hide480p;

    [ObservableProperty]
    private bool _hide720p;

    [ObservableProperty]
    private bool _hideNonRussian;

    [ObservableProperty]
    private string _manualToken = string.Empty;

    [ObservableProperty]
    private bool _isManualTokenExpanded;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string? _statusMessage;

    public SettingsStreamingViewModel(
        ISettingsService settingsService,
        Anime365HostResolver? hostResolver = null)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
        _hostResolver = hostResolver;

        var streaming = _settingsService.Current.Torrents.OnlineStreaming;
        var savedMirror = streaming.Anime365Mirror ?? string.Empty;
        _selectedAnime365Mirror = Anime365Mirrors.FirstOrDefault(m => string.Equals(m.Key, savedMirror, StringComparison.OrdinalIgnoreCase))
            ?? Anime365Mirrors[0];

        _isAnimeLibLoggedIn = !string.IsNullOrWhiteSpace(streaming.AnimeLibToken);
        _animeLibUsername = !string.IsNullOrWhiteSpace(streaming.AnimeLibUsername)
            ? streaming.AnimeLibUsername
            : (_isAnimeLibLoggedIn ? (AnimeLibTokenHelper.TryExtractUsername(streaming.AnimeLibToken) ?? "AnimeLib User") : string.Empty);
        _preferDirectMp4 = streaming.PreferDirectMp4;

        _isAnime365LoggedIn = !string.IsNullOrWhiteSpace(streaming.Anime365Token);
        _anime365Username = !string.IsNullOrWhiteSpace(streaming.Anime365Username)
            ? streaming.Anime365Username
            : (_isAnime365LoggedIn ? "Anime365 User" : string.Empty);

        _enableAniLibria = streaming.EnableAniLibria;
        _enableAnimeLib = streaming.EnableAnimeLib;
        _enableDreamCast = streaming.EnableDreamCast;
        _enableYummyAnime = streaming.EnableYummyAnime;
        _enableAnime365 = _isAnime365LoggedIn && streaming.EnableAnime365;
        _hide480p = streaming.Hide480p;
        _hide720p = streaming.Hide720p;
        _hideNonRussian = streaming.HideNonRussian;

        if (_isAnime365LoggedIn)
        {
            _ = CheckAnime365StatusSilentlyAsync(streaming.Anime365Token);
        }
    }

    [RelayCommand]
    private async Task LoginAnimeLibAsync()
    {
        if (IsLoggingIn) return;

        IsLoggingIn = true;
        StatusMessage = null;

        try
        {
            var lifetime = App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;

            var dlg = new AnimeLibLoginWindow();
            string? token;

            if (mainWindow != null)
            {
                token = await dlg.ShowDialog<string?>(mainWindow);
            }
            else
            {
                dlg.Show();
                return;
            }

            token = AnimeLibTokenHelper.CleanToken(token);
            if (!string.IsNullOrWhiteSpace(token))
            {
                var username = AnimeLibTokenHelper.TryExtractUsername(token) ?? "AnimeLib User";

                _settingsService.Update(s =>
                {
                    s.Torrents.OnlineStreaming.AnimeLibToken = token;
                    s.Torrents.OnlineStreaming.AnimeLibUsername = username;
                }, SettingsSection.Torrents);

                IsAnimeLibLoggedIn = true;
                AnimeLibUsername = username;
                IsManualTokenExpanded = false;
                StatusMessage = UIUtils.GetLoc("settings.streaming.success_login");
                Log.Information("[SettingsStreaming] AnimeLib token saved for user {User}", username);
            }
            else
            {
                Log.Information("[SettingsStreaming] AnimeLib login cancelled by user");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SettingsStreaming] Error opening AnimeLib login dialog");
            StatusMessage = string.Format(UIUtils.GetLoc("settings.streaming.error_prefix"), ex.Message);
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private void LogoutAnimeLib()
    {
        _settingsService.Update(s =>
        {
            s.Torrents.OnlineStreaming.AnimeLibToken = string.Empty;
            s.Torrents.OnlineStreaming.AnimeLibUsername = string.Empty;
        }, SettingsSection.Torrents);

        IsAnimeLibLoggedIn = false;
        AnimeLibUsername = string.Empty;
        ManualToken = string.Empty;
        StatusMessage = UIUtils.GetLoc("settings.streaming.success_logout");
        Log.Information("[SettingsStreaming] Logged out from AnimeLib");
    }

    [RelayCommand]
    private void ApplyManualToken()
    {
        if (string.IsNullOrWhiteSpace(ManualToken))
        {
            StatusMessage = UIUtils.GetLoc("settings.streaming.token_empty_error");
            return;
        }

        var cleanToken = AnimeLibTokenHelper.CleanToken(ManualToken);
        if (string.IsNullOrWhiteSpace(cleanToken))
        {
            StatusMessage = UIUtils.GetLoc("settings.streaming.token_empty_error");
            return;
        }

        var username = AnimeLibTokenHelper.TryExtractUsername(cleanToken) ?? "AnimeLib User";

        _settingsService.Update(s =>
        {
            s.Torrents.OnlineStreaming.AnimeLibToken = cleanToken;
            s.Torrents.OnlineStreaming.AnimeLibUsername = username;
        }, SettingsSection.Torrents);

        IsAnimeLibLoggedIn = true;
        AnimeLibUsername = username;
        ManualToken = string.Empty;
        IsManualTokenExpanded = false;
        StatusMessage = UIUtils.GetLoc("settings.streaming.token_saved");
    }

    [RelayCommand]
    private void ToggleManualToken()
    {
        IsManualTokenExpanded = !IsManualTokenExpanded;
    }

    [RelayCommand]
    private void ClearStatus()
    {
        StatusMessage = null;
    }

    partial void OnPreferDirectMp4Changed(bool value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.PreferDirectMp4 = value, SettingsSection.Torrents);
    }

    partial void OnEnableAniLibriaChanged(bool value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.EnableAniLibria = value, SettingsSection.Torrents);
    }

    partial void OnEnableAnimeLibChanged(bool value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.EnableAnimeLib = value, SettingsSection.Torrents);
    }

    partial void OnEnableDreamCastChanged(bool value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.EnableDreamCast = value, SettingsSection.Torrents);
    }

    partial void OnEnableYummyAnimeChanged(bool value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.EnableYummyAnime = value, SettingsSection.Torrents);
    }

    partial void OnHide480pChanged(bool value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.Hide480p = value, SettingsSection.Torrents);
    }

    partial void OnHide720pChanged(bool value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.Hide720p = value, SettingsSection.Torrents);
    }

    partial void OnHideNonRussianChanged(bool value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.HideNonRussian = value, SettingsSection.Torrents);
    }

    partial void OnEnableAnime365Changed(bool value)
    {
        if (value && !IsAnime365LoggedIn)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => EnableAnime365 = false);
            StatusMessage = UIUtils.GetLoc("settings.streaming.anime365_account_required");
            return;
        }

        _settingsService.Update(s => s.Torrents.OnlineStreaming.EnableAnime365 = value, SettingsSection.Torrents);
    }

    partial void OnSelectedAnime365MirrorChanged(Anime365MirrorOption value)
    {
        _settingsService.Update(s => s.Torrents.OnlineStreaming.Anime365Mirror = value.Key, SettingsSection.Torrents);
        if (!string.IsNullOrWhiteSpace(value.Key) && _hostResolver != null)
        {
            _hostResolver.RememberActiveMirror(value.Key);
        }
    }

    [RelayCommand]
    private async Task LoginAnime365Async()
    {
        if (IsAnime365LoggingIn) return;

        if (string.IsNullOrWhiteSpace(Anime365Email) || string.IsNullOrWhiteSpace(Anime365Password))
        {
            StatusMessage = UIUtils.GetLoc("settings.streaming.anime365_credentials_required");
            return;
        }

        IsAnime365LoggingIn = true;
        StatusMessage = null;

        try
        {
            var appId = _settingsService.Current.Torrents.OnlineStreaming.Anime365AppId;
            if (string.IsNullOrWhiteSpace(appId))
                appId = StreamingConstants.Anime365.DefaultAppId;

            var loginUrl = $"{StreamingConstants.Anime365.LoginEndpoint}?app={Uri.EscapeDataString(appId)}&email={Uri.EscapeDataString(Anime365Email.Trim())}&password={Uri.EscapeDataString(Anime365Password)}";
            if (_hostResolver != null)
            {
                loginUrl = _hostResolver.Rewrite(loginUrl);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, loginUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, "application/json");

            using var response = _hostResolver != null
                ? await _hostResolver.SendWithFailoverAsync(SharedHttpClient, request, default).ConfigureAwait(false)
                : await SharedHttpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                var msg = errorProp.TryGetProperty("message", out var mProp) ? mProp.GetString() : "Authentication failed";
                StatusMessage = string.Format(UIUtils.GetLoc("settings.streaming.error_prefix"), msg);
                return;
            }

            if (!doc.RootElement.TryGetProperty("data", out var dataProp) ||
                !dataProp.TryGetProperty("access_token", out var tokenProp) ||
                tokenProp.GetString() is not { } token ||
                string.IsNullOrWhiteSpace(token))
            {
                StatusMessage = UIUtils.GetLoc("settings.streaming.anime365_token_not_returned");
                return;
            }

            Anime365Password = string.Empty;
            await ApplyAnime365TokenInternalAsync(token.Trim()).ConfigureAwait(false);
            StatusMessage = UIUtils.GetLoc("settings.streaming.anime365_success_login");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SettingsStreaming] Error logging in to Anime365");
            StatusMessage = string.Format(UIUtils.GetLoc("settings.streaming.error_prefix"), ex.Message);
        }
        finally
        {
            IsAnime365LoggingIn = false;
        }
    }

    [RelayCommand]
    private async Task ApplyManualAnime365TokenAsync()
    {
        if (string.IsNullOrWhiteSpace(Anime365ManualToken))
        {
            StatusMessage = UIUtils.GetLoc("settings.streaming.token_empty_error");
            return;
        }

        var token = Anime365ManualToken.Trim();
        var success = await ApplyAnime365TokenInternalAsync(token).ConfigureAwait(false);
        if (success)
        {
            Anime365ManualToken = string.Empty;
            IsAnime365ManualTokenExpanded = false;
            StatusMessage = UIUtils.GetLoc("settings.streaming.anime365_token_saved");
        }
        else
        {
            StatusMessage = UIUtils.GetLoc("settings.streaming.anime365_invalid_token");
        }
    }

    private async Task<bool> ApplyAnime365TokenInternalAsync(string token)
    {
        try
        {
            var meUrl = $"{StreamingConstants.Anime365.MeEndpoint}?access_token={Uri.EscapeDataString(token)}";
            if (_hostResolver != null)
            {
                meUrl = _hostResolver.Rewrite(meUrl);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, meUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, "application/json");

            using var response = _hostResolver != null
                ? await _hostResolver.SendWithFailoverAsync(SharedHttpClient, request, default).ConfigureAwait(false)
                : await SharedHttpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp))
                return false;

            var isLogined = dataProp.TryGetProperty("isLogined", out var logProp) && logProp.GetBoolean();
            if (!isLogined)
                return false;

            var username = dataProp.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "Anime365 User";
            var isPremium = dataProp.TryGetProperty("isPremium", out var premProp) && premProp.GetBoolean();
            var premiumUntil = dataProp.TryGetProperty("premiumUntil", out var untilProp) ? untilProp.GetString() : null;

            _settingsService.Update(s =>
            {
                s.Torrents.OnlineStreaming.Anime365Token = token;
                s.Torrents.OnlineStreaming.Anime365Username = username ?? "Anime365 User";
            }, SettingsSection.Torrents);

            IsAnime365LoggedIn = true;
            IsAnime365ManualTokenExpanded = false;
            Anime365Username = username ?? "Anime365 User";
            Anime365StatusText = FormatPremiumStatus(isPremium, premiumUntil);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[SettingsStreaming] Failed to verify Anime365 token");
            return false;
        }
    }

    private async Task CheckAnime365StatusSilentlyAsync(string token)
    {
        try
        {
            var meUrl = $"{StreamingConstants.Anime365.MeEndpoint}?access_token={Uri.EscapeDataString(token)}";
            if (_hostResolver != null)
            {
                meUrl = _hostResolver.Rewrite(meUrl);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, meUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, "application/json");

            using var response = _hostResolver != null
                ? await _hostResolver.SendWithFailoverAsync(SharedHttpClient, request, default).ConfigureAwait(false)
                : await SharedHttpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp)) return;

            var isLogined = dataProp.TryGetProperty("isLogined", out var logProp) && logProp.GetBoolean();
            if (!isLogined)
            {
                IsAnime365LoggedIn = false;
                return;
            }

            var username = dataProp.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "Anime365 User";
            var isPremium = dataProp.TryGetProperty("isPremium", out var premProp) && premProp.GetBoolean();
            var premiumUntil = dataProp.TryGetProperty("premiumUntil", out var untilProp) ? untilProp.GetString() : null;

            Anime365Username = username ?? "Anime365 User";
            Anime365StatusText = FormatPremiumStatus(isPremium, premiumUntil);
        }
        catch
        {
            // Ignore background check errors
        }
    }

    private static string FormatPremiumStatus(bool isPremium, string? premiumUntil)
    {
        if (isPremium)
        {
            return !string.IsNullOrWhiteSpace(premiumUntil)
                ? string.Format(UIUtils.GetLoc("settings.streaming.anime365_premium_until"), premiumUntil)
                : UIUtils.GetLoc("settings.streaming.anime365_premium_active");
        }
        return UIUtils.GetLoc("settings.streaming.anime365_no_premium");
    }

    [RelayCommand]
    private void LogoutAnime365()
    {
        _settingsService.Update(s =>
        {
            s.Torrents.OnlineStreaming.Anime365Token = string.Empty;
            s.Torrents.OnlineStreaming.Anime365Username = string.Empty;
            s.Torrents.OnlineStreaming.EnableAnime365 = false;
        }, SettingsSection.Torrents);

        IsAnime365LoggedIn = false;
        IsAnime365ManualTokenExpanded = false;
        EnableAnime365 = false;
        Anime365Username = string.Empty;
        Anime365StatusText = string.Empty;
        Anime365ManualToken = string.Empty;
        StatusMessage = UIUtils.GetLoc("settings.streaming.anime365_success_logout");
        Log.Information("[SettingsStreaming] Logged out from Anime365");
    }

    [RelayCommand]
    private void ToggleAnime365ManualToken()
    {
        IsAnime365ManualTokenExpanded = !IsAnime365ManualTokenExpanded;
    }
}

public sealed record Anime365MirrorOption(string Key, string DisplayName);
