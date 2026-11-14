using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kiriha.Mpv;
using Kiriha.Mpv.UI.ViewModels.Player;

namespace Kiriha.Views.Player;

public partial class PlayerWindow
{


    private void OnTimelinePointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control timeline || DataContext is not PlayerViewModel vm || vm.Duration <= 0)
            return;

        var now = DateTime.UtcNow;
        var timelinePos = e.GetPosition(timeline);
        var ratio = Math.Clamp(timelinePos.X / Math.Max(1, timeline.Bounds.Width), 0, 1);
        var previewTime = ratio * vm.Duration;

        if ((now - _lastTimelinePreviewAt).TotalMilliseconds < 80 &&
            Math.Abs(previewTime - _lastTimelinePreviewTime) < 1)
            return;

        _lastTimelinePreviewAt = now;
        _lastTimelinePreviewTime = previewTime;

        var bottomPos = _bottomBar != null ? e.GetPosition(_bottomBar) : timelinePos;
        double popupWidth = _timelinePreviewBorder is { Bounds.Width: > 1 } b
            ? b.Bounds.Width
            : (vm.Overlay.TimelinePreviewImage != null ? 236.0 : 72.0);
        var maxLeft = Math.Max(8, (_bottomBar?.Bounds.Width ?? Bounds.Width) - popupWidth - 8);
        var previewLeft = Math.Clamp(bottomPos.X - (popupWidth / 2.0), 8, maxLeft);
        vm.ShowTimelinePreview(previewTime, previewLeft);

        _lastPointerPosition = e.GetPosition(this);
        var nowUtc = DateTime.UtcNow;
        if (_controlsVisible && nowUtc - _lastControlsKeepAliveUtc < ControlsKeepAliveInterval)
            return;

        _lastControlsKeepAliveUtc = nowUtc;

        if (!_controlsVisible)
        {
            ShowControls();
            return;
        }

        // Fast-path: if controls are already visible, just restart the timer
        ResetHideTimer();
    }

    private void OnTimelinePointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.HideTimelinePreview();
    }

    // Window controls

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        OnFullscreenClick(sender, e);
    }

    private WindowState _previousWindowState = WindowState.Normal;

    private void OnFullscreenClick(object? sender, EventArgs e)
    {
        if (this.WindowState == WindowState.FullScreen)
        {
            this.WindowState = _previousWindowState == WindowState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
        }
        else
        {
            _previousWindowState = this.WindowState;
            this.WindowState = WindowState.FullScreen;
        }
    }

    private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowSettingsOverlay();
    }
}
