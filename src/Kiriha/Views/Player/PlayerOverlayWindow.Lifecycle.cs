using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Kiriha.ViewModels.Player;

namespace Kiriha.Views.Player;

public partial class PlayerOverlayWindow
{
    private void DisableLegacySettingsFlyout()
    {
        if (_settingsButton != null)
            _settingsButton.Flyout = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer.Stop();
        _hideTimer.Tick -= OnHideTimerTick;
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

        if (_subscribedViewModel != null)
        {
            if (_viewModelPropertyChanged != null)
                _subscribedViewModel.PropertyChanged -= _viewModelPropertyChanged;
        }

        if (_ownerWindow != null)
        {
            if (_ownerPositionChanged != null)
                _ownerWindow.PositionChanged -= _ownerPositionChanged;
            if (_ownerPropertyChanged != null)
                _ownerWindow.PropertyChanged -= _ownerPropertyChanged;
        }

        _subscribedViewModel = null;
        _viewModelPropertyChanged = null;
        _ownerPositionChanged = null;
        _ownerPropertyChanged = null;

        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.AutoHideControls)
                 && sender is PlayerViewModel { AutoHideControls: false })
            ShowControls();
    }

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.BoundsProperty || e.Property == Window.ClientSizeProperty)
        {
            UpdateOverlayPosition();
        }
        else if (e.Property == Window.WindowStateProperty)
        {
            WindowState = (WindowState)e.NewValue!;
            UpdateCornerRounding();
        }
    }

    private void UpdateCornerRounding()
    {
        bool isFullscreen = WindowState == WindowState.FullScreen;
        bool isEdgeToEdge = isFullscreen || WindowState == WindowState.Maximized;

        if (_maximizeIcon != null)
        {
            _maximizeIcon.Text = isFullscreen
                ? "\uE923"
                : "\uE922";
        }

        // Remove rounded corners from the actual window by changing decorations and client area hint
        if (_ownerWindow != null)
        {
            _ownerWindow.WindowDecorations = isEdgeToEdge ? WindowDecorations.None : WindowDecorations.BorderOnly;
            _ownerWindow.ExtendClientAreaToDecorationsHint = !isEdgeToEdge;
        }

        // Remove rounded corner from the close button so it stays flush
        if (_closeButton != null)
        {
            _closeButton.CornerRadius = isEdgeToEdge ? new CornerRadius(0) : new CornerRadius(0, 8, 0, 0);
        }

        // Remove rounded corners from the Top and Bottom shadow gradients
        if (_topBar != null)
        {
            _topBar.CornerRadius = isEdgeToEdge ? new CornerRadius(0) : new CornerRadius(8, 8, 0, 0);
        }
        if (_bottomBar != null)
        {
            _bottomBar.CornerRadius = isEdgeToEdge ? new CornerRadius(0) : new CornerRadius(0, 0, 8, 8);
        }
    }


    // ──────────────────────────────────────────────────────────
    // Auto-hide logic
    // ──────────────────────────────────────────────────────────
}
