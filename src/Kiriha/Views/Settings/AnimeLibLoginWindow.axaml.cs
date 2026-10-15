using System.Drawing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Helpers;
using Kiriha.Infrastructure.Platform;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Kiriha.Views.Settings;

public partial class AnimeLibLoginWindow : Window
{
    private CoreWebView2Controller? _controller;
    private DispatcherTimer? _pollTimer;
    private bool _authCompleted;
    private bool _isDisposed;

    public AnimeLibLoginWindow()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (!OperatingSystem.IsWindows())
        {
            StatusTextBlock.Text = Kiriha.Core.UIUtils.GetLoc("settings.streaming.error_windows_only");
            return;
        }

        await InitializeWebViewAsync().ConfigureAwait(true);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _pollTimer?.Stop();
        _pollTimer = null;

        CleanupWebView();
        base.OnClosing(e);
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var platformHandle = TryGetPlatformHandle();
            if (platformHandle == null)
            {
                StatusTextBlock.Text = Kiriha.Core.UIUtils.GetLoc("settings.streaming.error_no_handle");
                return;
            }

            var hwnd = platformHandle.Handle;
            var userDataFolder = Path.Combine(PathHelper.GetSettingsDirPath(), "webview_animelib");
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
            _controller = await env.CreateCoreWebView2ControllerAsync(hwnd).ConfigureAwait(true);

            UpdateBounds();
            _controller.IsVisible = true;

            var core = _controller.CoreWebView2;

            // 1. Intercept Authorization Bearer header from outgoing/incoming API requests
            core.WebResourceResponseReceived += OnWebResourceResponseReceived;

            // 2. Check localStorage on navigation completed
            core.NavigationCompleted += OnNavigationCompleted;

            // 3. Periodic fallback check every 2 seconds
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _pollTimer.Tick += async (_, _) => await CheckStorageTokenAsync().ConfigureAwait(true);
            _pollTimer.Start();

            // Navigate to official AnimeLib login
            core.NavigationStarting += (_, args) =>
            {
                Log.Information("[AnimeLibLoginWindow] Navigating to {Uri}", args.Uri);
            };
            core.Navigate(StreamingConstants.AnimeLib.LoginUrl);

            this.SizeChanged += (_, _) => UpdateBounds();
            this.LayoutUpdated += (_, _) => UpdateBounds();
            var host = this.FindControl<Grid>("HostGrid");
            if (host != null) host.SizeChanged += (_, _) => UpdateBounds();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            StatusTextBlock.Text = Kiriha.Core.UIUtils.GetLoc("settings.streaming.error_webview_missing");
            LoadingIndicator.IsVisible = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AnimeLibLoginWindow] Failed to initialize WebView2");
            StatusTextBlock.Text = ex.Message;
        }
    }

    private void UpdateBounds()
    {
        if (_controller == null || _isDisposed)
            return;

        try
        {
            var scale = RenderScaling;
            var host = this.FindControl<Grid>("HostGrid");
            if (host == null) return;

            var point = host.TranslatePoint(new Avalonia.Point(0, 0), this) ?? new Avalonia.Point(0, 45);
            var x = (int)(point.X * scale);
            var y = (int)(point.Y * scale);
            var w = (int)(host.Bounds.Width * scale);
            var h = (int)(host.Bounds.Height * scale);

            if (w <= 0 || h <= 0)
            {
                x = 0;
                y = (int)(45 * scale);
                w = (int)(ClientSize.Width * scale);
                h = Math.Max(100, (int)((ClientSize.Height - 85) * scale));
            }

            if (w > 0 && h > 0)
            {
                _controller.Bounds = new Rectangle(x, y, w, h);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[AnimeLibLoginWindow] Error updating WebView2 bounds");
        }
    }

    private void OnWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (_authCompleted) return;

        try
        {
            // Inspect request headers for Bearer token
            if (e.Request.Headers.Contains("Authorization"))
            {
                var authHeader = e.Request.Headers.GetHeader("Authorization");
                if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader["Bearer ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        CompleteAuth(token);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[AnimeLibLoginWindow] Error in WebResourceResponseReceived");
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_authCompleted) return;
        await CheckStorageTokenAsync().ConfigureAwait(true);
    }

    private async Task CheckStorageTokenAsync()
    {
        if (_authCompleted || _controller == null || _isDisposed)
            return;

        try
        {
            var core = _controller.CoreWebView2;

            // Script to query token from localStorage
            var script = @"(function() {
                try {
                    for (var i = 0; i < localStorage.length; i++) {
                        var key = localStorage.key(i);
                        var val = localStorage.getItem(key);
                        if (!val) continue;
                        if (key === 'token' || key === 'auth_token' || key === 'access_token') return val;
                        if (typeof val === 'string' && val.indexOf('ey') === 0 && val.split('.').length === 3) return val;
                        try {
                            var obj = JSON.parse(val);
                            if (obj && typeof obj === 'object') {
                                if (obj.token) return obj.token;
                                if (obj.access_token) return obj.access_token;
                                if (obj.auth_token) return obj.auth_token;
                            }
                        } catch(e) {}
                    }
                    return '';
                } catch(e) { return ''; }
            })()";
            var result = await core.ExecuteScriptAsync(script).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(result) && result != "\"\"" && result != "null")
            {
                var token = result.Trim('"');
                if (!string.IsNullOrWhiteSpace(token))
                {
                    CompleteAuth(token);
                    return;
                }
            }

            // Check cookies as fallback
            var cookies = await core.CookieManager.GetCookiesAsync(StreamingConstants.AnimeLib.SiteUrl).ConfigureAwait(true);
            foreach (var c in cookies)
            {
                if (c.Name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Equals("auth_token", StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Equals("access_token", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(c.Value))
                    {
                        CompleteAuth(c.Value);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[AnimeLibLoginWindow] Error checking storage token");
        }
    }

    private void CompleteAuth(string token)
    {
        if (_authCompleted) return;
        _authCompleted = true;

        var clean = AnimeLibTokenHelper.CleanToken(token);
        Log.Information("[AnimeLibLoginWindow] Successfully captured AnimeLib session token (len: {Len})", clean.Length);

        Dispatcher.UIThread.Post(() =>
        {
            Close(clean);
        });
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void CleanupWebView()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            if (_controller != null)
            {
                _controller.Close();
                _controller = null;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[AnimeLibLoginWindow] Error disposing WebView2 controller");
        }
    }
}
