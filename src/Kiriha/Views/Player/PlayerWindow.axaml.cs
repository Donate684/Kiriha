using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kiriha.Mpv;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Settings;
using Kiriha.ViewModels.Player;
using Serilog;

namespace Kiriha.Views.Player;

public partial class PlayerWindow : Window
{
    private static readonly Cursor s_arrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor s_noneCursor = new(StandardCursorType.None);

    private static readonly HashSet<string> DropMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg", ".ogm", ".ogg",
        ".ass", ".srt"
    };

    // Chapter markers
    private Border? _topBar;
    private Border? _bottomBar;
    private Slider? _timelineSlider;
    private Views.Player.Controls.PlayerControlBar? _controlBar;
    private Button? _settingsButton;
    private Button? _screenshotButton;
    private Button? _closeButton;
    private TextBlock? _maximizeIcon;
    
    private PlayerViewModel? _subscribedViewModel;
    private PropertyChangedEventHandler? _viewModelPropertyChanged;
    private DateTime _lastTimelinePreviewAt = DateTime.MinValue;
    private double _lastTimelinePreviewTime = -1;

    private MpvPlayer? _player;
    private PlayerLoadingPipeline? _loadingPipeline;

    public MpvPlayer? Player => _player;

    private readonly Kiriha.Core.Abstractions.Services.ISettingsService? _settingsService;

    public PlayerWindow()
    {
        InitializeComponent();
        CacheOverlayControls();
        DisableLegacySettingsFlyout();
        InitializeAutoHide();
        
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnOverlayKeyDown, RoutingStrategies.Tunnel);

        if (_timelineSlider != null)
        {
            _timelineSlider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
            _timelineSlider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
        }

        if (_screenshotButton != null)
            _screenshotButton.AddHandler(PointerReleasedEvent, OnScreenshotButtonPointerReleased, RoutingStrategies.Tunnel);
    }

    public PlayerWindow(Kiriha.Core.Abstractions.Services.ISettingsService settingsService) : this()
    {
        _settingsService = settingsService;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Focus();

        if (DataContext is PlayerViewModel vm)
        {
            _subscribedViewModel = vm;
            _viewModelPropertyChanged = OnViewModelPropertyChanged;
            vm.PropertyChanged += _viewModelPropertyChanged;
        }

        StartAutoHide();

        try
        {
            Log.Information("Initializing MPV player with libmpv render API");
            var playerSettings = _settingsService?.Current.Player;
            if (playerSettings == null) return;
            var mpvOptions = new MpvOptions(
                playerSettings.MpvHwdec,
                playerSettings.MpvVideoOutput,
                playerSettings.MpvGpuApi,
                playerSettings.MpvGpuContext,
                VideoSync: playerSettings.MpvVideoSync ? "display-resample" : "no",
                Interpolation: playerSettings.MpvInterpolation);

            _player = MpvPlayerBuilder.Build(mpvOptions);

            if (DataContext is PlayerViewModel playerVm)
            {
                _loadingPipeline = new PlayerLoadingPipeline(playerVm, VideoHost);
            }

            VideoHost.RenderContextReady += OnVideoRenderContextReady;
            if (_loadingPipeline != null)
            {
                _loadingPipeline.AttachPlayer(_player);
            }
            else
            {
                VideoHost.Player = _player;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize MPV player");
        }
    }

    private void OnVideoRenderContextReady(object? sender, EventArgs e)
    {
        _loadingPipeline?.MarkRenderContextReady();
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        VideoHost.RequestNextFrameRendering();
    }

    protected override void OnClosed(EventArgs e)
    {
        VideoHost.RenderContextReady -= OnVideoRenderContextReady;

        var dataContext = DataContext;
        DataContext = null;

        try
        {
            _hideTimer.Stop();
            _hideTimer.Tick -= OnHideTimerTick;
            
            _hitTestDisableTimer.Stop();
            _hitTestDisableTimer.Tick -= OnHitTestDisableTimerTick;

            if (_leftClickTimer != null)
            {
                _leftClickTimer.Stop();
                _leftClickTimer = null;
            }

            RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            RemoveHandler(DragDrop.DropEvent, OnDrop);
            RemoveHandler(KeyDownEvent, OnOverlayKeyDown);

            if (_timelineSlider != null)
            {
                _timelineSlider.RemoveHandler(PointerPressedEvent, OnSliderPointerPressed);
                _timelineSlider.RemoveHandler(PointerReleasedEvent, OnSliderPointerReleased);
            }

            if (_screenshotButton != null)
                _screenshotButton.RemoveHandler(PointerReleasedEvent, OnScreenshotButtonPointerReleased);

            if (_subscribedViewModel != null && _viewModelPropertyChanged != null)
            {
                _subscribedViewModel.PropertyChanged -= _viewModelPropertyChanged;
            }
            _subscribedViewModel = null;
            _viewModelPropertyChanged = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clean up overlay resources");
        }
        finally
        {
            _loadingPipeline = null;

            try
            {
                if (dataContext is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to dispose PlayerViewModel");
            }
            finally
            {
                try
                {
                    _player?.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to dispose MpvPlayer");
                }
                finally
                {
                    _player = null;
                }
            }
        }

        base.OnClosed(e);

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow == this)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
            }
        }
    }

    private void CacheOverlayControls()
    {
        _topBar = this.FindControl<Border>("TopBar");
        _bottomBar = this.FindControl<Border>("BottomBar");
        _timelineSlider = this.FindControl<Slider>("TimelineSlider");
        _controlBar = this.FindControl<Views.Player.Controls.PlayerControlBar>("ControlBar");
        _settingsButton = this.FindControl<Button>("SettingsButton");
        _screenshotButton = this.FindControl<Button>("ScreenshotButton");
        _closeButton = this.FindControl<Button>("CloseButton");
        _maximizeIcon = this.FindControl<TextBlock>("MaximizeIcon");
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetDroppedMediaPath(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!TryGetDroppedMediaPath(e, out var path)) return;

        Log.Information("Dropped media file: {Path}", path);
        if (DataContext is PlayerViewModel vm)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".ass" or ".srt")
                vm.LoadSubtitle(path);
            else
                vm.LoadVideo(path);
                
            ShowControls();
        }
    }

    private static bool TryGetDroppedMediaPath(DragEventArgs e, out string path)
    {
        path = string.Empty;
        var files = e.DataTransfer.TryGetFiles();
        var localPath = files?.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath) || !System.IO.File.Exists(localPath)) return false;
        if (!DropMediaExtensions.Contains(System.IO.Path.GetExtension(localPath))) return false;

        path = localPath;
        return true;
    }

    private void OnSettingsClicked(object? sender, EventArgs e) => ShowSettingsOverlay();
    private void OnActionExecuted(object? sender, EventArgs e) => ShowControls();
    private void OnSettingsOverlayClosed(object? sender, EventArgs e) => Focus();
}
