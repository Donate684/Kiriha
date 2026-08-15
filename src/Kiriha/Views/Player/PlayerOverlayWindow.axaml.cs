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
using Kiriha.ViewModels.Player;
using Serilog;

namespace Kiriha.Views.Player;

public partial class PlayerOverlayWindow : Window
{
    private static readonly Cursor s_arrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor s_noneCursor = new(StandardCursorType.None);

    private PlayerWindow _ownerWindow;
    private static readonly HashSet<string> DropMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg", ".ogm", ".ogg",
        ".ass", ".srt"
    };



    // Chapter markers
    private Border? _topBar;
    private Border? _bottomBar;

    private Slider? _timelineSlider;

    private Controls.PlayerControlBar? _controlBar;
    private Button? _settingsButton;
    private Button? _screenshotButton;
    private Button? _closeButton;
    private TextBlock? _maximizeIcon;
    private PlayerViewModel? _subscribedViewModel;
    private PropertyChangedEventHandler? _viewModelPropertyChanged;
    private EventHandler<PixelPointEventArgs>? _ownerPositionChanged;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _ownerPropertyChanged;
    private DateTime _lastTimelinePreviewAt = DateTime.MinValue;
    private double _lastTimelinePreviewTime = -1;
    private bool _positionUpdatePending;

    public PlayerOverlayWindow()
    {
        InitializeComponent();
        CacheOverlayControls();
        DisableLegacySettingsFlyout();
        _ownerWindow = null!;
        InitializeAutoHide();
        AddHandler(KeyDownEvent, OnOverlayKeyDown, RoutingStrategies.Tunnel);
    }

    public PlayerOverlayWindow(PlayerWindow owner)
    {
        InitializeComponent();
        CacheOverlayControls();
        DisableLegacySettingsFlyout();
        _ownerWindow = owner;
        DataContext = owner.DataContext;

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

        // Subscribe to chapter changes from the ViewModel
        if (DataContext is PlayerViewModel vm)
        {
            _subscribedViewModel = vm;
            _viewModelPropertyChanged = OnViewModelPropertyChanged;

            vm.PropertyChanged += _viewModelPropertyChanged;
        }

        // Keep overlay synced with owner window
        _ownerPositionChanged = (_, _) => ScheduleOverlayPositionUpdate();
        _ownerPropertyChanged = OnOwnerPropertyChanged;
        _ownerWindow.PositionChanged += _ownerPositionChanged;
        _ownerWindow.PropertyChanged += _ownerPropertyChanged;

        StartAutoHide();
    }

    private void CacheOverlayControls()
    {
        _topBar = this.FindControl<Border>("TopBar");
        _bottomBar = this.FindControl<Border>("BottomBar");

        _timelineSlider = this.FindControl<Slider>("TimelineSlider");

        _controlBar = this.FindControl<Controls.PlayerControlBar>("ControlBar");
        _settingsButton = this.FindControl<Button>("SettingsButton");
        _screenshotButton = this.FindControl<Button>("ScreenshotButton");
        _closeButton = this.FindControl<Button>("CloseButton");
        _maximizeIcon = this.FindControl<TextBlock>("MaximizeIcon");
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetDroppedMediaPath(e, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!TryGetDroppedMediaPath(e, out var path))
            return;

        Log.Information("Dropped media file: {Path}", path);
        if (DataContext is PlayerViewModel vm)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".ass" or ".srt")
            {
                vm.LoadSubtitle(path);
            }
            else
            {
                vm.LoadVideo(path);
            }
            ShowControls();
        }
    }

    private static bool TryGetDroppedMediaPath(DragEventArgs e, out string path)
    {
        path = string.Empty;
        var files = e.DataTransfer.TryGetFiles();
        var firstFile = files?.FirstOrDefault();
        var localPath = firstFile?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath) || !System.IO.File.Exists(localPath))
            return false;

        if (!DropMediaExtensions.Contains(System.IO.Path.GetExtension(localPath)))
            return false;

        path = localPath;
        return true;
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        ShowSettingsOverlay();
    }

    private void OnActionExecuted(object? sender, EventArgs e)
    {
        ShowControls();
    }

    private void OnSettingsOverlayClosed(object? sender, EventArgs e)
    {
        Focus();
    }

    private void ScheduleOverlayPositionUpdate()
    {
        if (_positionUpdatePending) return;
        _positionUpdatePending = true;
        Dispatcher.UIThread.Post(ApplyOverlayPosition, DispatcherPriority.Render);
    }

    private void ApplyOverlayPosition()
    {
        _positionUpdatePending = false;
        UpdateOverlayPosition();
    }
}
