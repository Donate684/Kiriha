using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kiriha.Core.Mpv;
using Kiriha.ViewModels.Player;

namespace Kiriha.Views.Player;

public partial class PlayerOverlayWindow
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
        var maxLeft = Math.Max(8, (_bottomBar?.Bounds.Width ?? Bounds.Width) - 244);
        var previewLeft = Math.Clamp(bottomPos.X - 118, 8, maxLeft);
        vm.ShowTimelinePreview(previewTime, previewLeft);
        ShowControls();
    }

    private void OnTimelinePointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.HideTimelinePreview();
    }

    // ──────────────────────────────────────────────────────────
    // Overlay positioning
    // ──────────────────────────────────────────────────────────

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateOverlayPosition();
        Focus();
    }

    private void UpdateOverlayPosition()
    {
        if (_ownerWindow == null) return;

        var clientPos = _ownerWindow.PointToScreen(new Point(0, 0));
        Position = clientPos;

        var clientSize = _ownerWindow.ClientSize;
        Width = clientSize.Width;
        Height = clientSize.Height;
    }

    // ──────────────────────────────────────────────────────────
    // Window controls
    // ──────────────────────────────────────────────────────────

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _ownerWindow.Close();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        _ownerWindow.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        OnFullscreenClick(sender, e);
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e)
    {
        _ownerWindow.WindowState = _ownerWindow.WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowSettingsOverlay();
    }

    private void OnScreenshotButtonPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        switch (properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonReleased:
                e.Handled = true;
                vm.TakeScreenshot(includeSubtitles: false);
                ShowControls();
                break;
            case PointerUpdateKind.RightButtonReleased:
                e.Handled = true;
                vm.TakeScreenshot(includeSubtitles: true);
                ShowControls();
                break;
        }
    }

    private void OnSubtitlePositionButtonPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        switch (properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonReleased:
                e.Handled = true;
                vm.MoveSubtitleUp();
                ShowControls();
                break;
            case PointerUpdateKind.RightButtonReleased:
                e.Handled = true;
                vm.MoveSubtitleDown();
                ShowControls();
                break;
        }
    }

    private void OnTrackMenuItemClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: TrackInfo track } || DataContext is not PlayerViewModel vm)
            return;

        vm.SelectTrackCommand.Execute(track);

        var flyoutButtonName = track.Type == "sub" ? "SubtitleButton" : "AudioButton";
        this.FindControl<Button>(flyoutButtonName)?.Flyout?.Hide();
        ShowControls();
    }
}
