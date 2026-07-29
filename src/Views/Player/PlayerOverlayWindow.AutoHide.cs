using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Kiriha.ViewModels.Player;

namespace Kiriha.Views.Player;

public partial class PlayerOverlayWindow
{
    // Auto-hide: hide panels after timeout of no mouse movement
    private static readonly TimeSpan ControlsKeepAliveInterval = TimeSpan.FromMilliseconds(180);
    private readonly DispatcherTimer _hideTimer = new();
    private bool _controlsVisible = true;
    private DateTime _lastControlsKeepAliveUtc = DateTime.MinValue;

    private void UpdateHideTimerInterval()
    {
        if (DataContext is PlayerViewModel vm)
            _hideTimer.Interval = TimeSpan.FromSeconds(vm.AutoHideTimeout);
        else
            _hideTimer.Interval = TimeSpan.FromSeconds(1.5);
    }

    private void InitializeAutoHide()
    {
        _hideTimer.Tick += OnHideTimerTick;
        UpdateHideTimerInterval();
    }

    private void StartAutoHide()
    {
        UpdateHideTimerInterval();
        _hideTimer.Start();
    }

    private void ShowControls()
    {
        var now = DateTime.UtcNow;
        bool wasHidden = !_controlsVisible;
        if (wasHidden)
        {
            _controlsVisible = true;
            if (_topBar != null)
            {
                _topBar.Opacity = 1;
                _topBar.IsHitTestVisible = true;
            }
            if (_bottomBar != null)
            {
                _bottomBar.Opacity = 1;
                _bottomBar.IsHitTestVisible = true;
            }
            Cursor = s_arrowCursor;
        }

        if (!wasHidden && now - _lastControlsKeepAliveUtc < ControlsKeepAliveInterval)
            return;

        _lastControlsKeepAliveUtc = now;
        // Reset the hide timer
        _hideTimer.Stop();
        UpdateHideTimerInterval();
        _hideTimer.Start();
    }

    private async void HideControls()
    {
        _controlsVisible = false;
        _lastControlsKeepAliveUtc = DateTime.MinValue;
        if (_topBar != null) _topBar.Opacity = 0;
        if (_bottomBar != null) _bottomBar.Opacity = 0;
        Cursor = s_noneCursor;

        await System.Threading.Tasks.Task.Delay(300);

        if (!_controlsVisible)
        {
            if (_topBar != null) _topBar.IsHitTestVisible = false;
            if (_bottomBar != null) _bottomBar.IsHitTestVisible = false;
        }
    }

    private bool IsPointerOverUI()
    {
        if (_topBar?.IsPointerOver == true || _bottomBar?.IsPointerOver == true)
            return true;

        if (IsSettingsOverlayVisible())
            return true;

        if (this.FindControl<Button>("SpeedButton")?.Flyout?.IsOpen == true)
            return true;

        if (this.FindControl<Button>("SubtitleButton")?.Flyout?.IsOpen == true)
            return true;

        if (this.FindControl<Button>("AudioButton")?.Flyout?.IsOpen == true)
            return true;

        if (this.FindControl<Button>("SettingsButton")?.Flyout?.IsOpen == true)
            return true;

        return false;
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        if (DataContext is PlayerViewModel { AutoHideControls: false })
            return;

        if (DataContext is PlayerViewModel vm && !vm.IsPlaying && !string.IsNullOrEmpty(vm.VideoUrl))
        {
            // Do not hide controls if paused and video is loaded
            return;
        }

        if (IsPointerOverUI())
            return;

        HideControls();
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        ShowControls();
    }

    private void OnGridPointerExited(object? sender, PointerEventArgs e)
    {
        // When the pointer leaves the grid (e.g., moving outside the window or into a flyout popup), 
        // we do not hide the controls instantly. We rely on the hide timer to expire and hide them 
        // naturally via timeout, ensuring that hovering over flyouts doesn't dismiss the UI.
    }
}
