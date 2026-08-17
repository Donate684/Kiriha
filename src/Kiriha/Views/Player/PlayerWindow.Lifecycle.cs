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

public partial class PlayerWindow
{
    private void DisableLegacySettingsFlyout()
    {
        if (_settingsButton != null)
            _settingsButton.Flyout = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.AutoHideControls)
                 && sender is PlayerViewModel { AutoHideControls: false })
            ShowControls();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
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
        if (this != null)
        {
            this.WindowDecorations = isEdgeToEdge ? WindowDecorations.None : WindowDecorations.BorderOnly;
            this.ExtendClientAreaToDecorationsHint = !isEdgeToEdge;
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
