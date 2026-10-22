using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Kiriha.Mpv.UI.ViewModels.Player;

namespace Kiriha.Views.Player;

public partial class PlayerWindow
{
    // Auto-hide: hide panels after timeout of no mouse movement
    private static readonly TimeSpan ControlsKeepAliveInterval = TimeSpan.FromMilliseconds(180);
    private const double ShowControlsMovementThreshold = 25.0;
    private const double ShowControlsMovementThresholdSquared = ShowControlsMovementThreshold * ShowControlsMovementThreshold;

    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherTimer _hitTestDisableTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private bool _controlsVisible = true;
    private DateTime _lastControlsKeepAliveUtc = DateTime.MinValue;
    private Point? _lastPointerPosition;
    private Point? _hiddenPointerPosition;

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
        _hitTestDisableTimer.Tick += OnHitTestDisableTimerTick;
        UpdateHideTimerInterval();
    }

    private void StartAutoHide()
    {
        UpdateHideTimerInterval();
        _hideTimer.Start();
    }

    private void ResetHideTimer()
    {
        if (!_controlsVisible) return;
        _hideTimer.Stop();
        UpdateHideTimerInterval();
        _hideTimer.Start();
    }

    private void ShowControls()
    {
        _hitTestDisableTimer.Stop();
        _hiddenPointerPosition = null;

        var now = DateTime.UtcNow;
        bool wasHidden = !_controlsVisible;
        if (wasHidden || (_topBar != null && !_topBar.IsVisible) || (_bottomBar != null && !_bottomBar.IsVisible))
        {
            _controlsVisible = true;
            if (_topBar != null)
            {
                _topBar.IsVisible = true;
                _topBar.Opacity = 1;
                _topBar.IsHitTestVisible = true;
            }
            if (_bottomBar != null)
            {
                _bottomBar.IsVisible = true;
                _bottomBar.Opacity = 1;
                _bottomBar.IsHitTestVisible = true;
            }
            Cursor = s_arrowCursor;
        }

        _lastControlsKeepAliveUtc = now;
        ResetHideTimer();
    }

    private void HideControls()
    {
        _controlsVisible = false;
        _lastControlsKeepAliveUtc = DateTime.MinValue;
        _hiddenPointerPosition = _lastPointerPosition;
        if (_topBar != null && !(DataContext is PlayerViewModel vm && (vm.Overlay.IsOsdVisible || vm.IsBufferingOrLoading)))
        {
            _topBar.Opacity = 0;
        }
        if (_bottomBar != null) _bottomBar.Opacity = 0;
        Cursor = s_noneCursor;

        _hitTestDisableTimer.Stop();
        _hitTestDisableTimer.Start();
    }

    private void OnHitTestDisableTimerTick(object? sender, EventArgs e)
    {
        _hitTestDisableTimer.Stop();
        if (!_controlsVisible)
        {
            if (_topBar != null)
            {
                if (DataContext is PlayerViewModel vm && (vm.Overlay.IsOsdVisible || vm.IsBufferingOrLoading))
                {
                    _topBar.IsHitTestVisible = true;
                    _topBar.IsVisible = true;
                }
                else
                {
                    _topBar.IsHitTestVisible = false;
                    _topBar.IsVisible = false;
                }
            }
            if (_bottomBar != null)
            {
                _bottomBar.IsHitTestVisible = false;
                _bottomBar.IsVisible = false;
            }
        }
    }

    private bool IsPointerOverUI()
    {
        if (IsSettingsOverlayVisible())
            return true;

        if (_controlBar?.IsAnyFlyoutOpen() == true)
            return true;

        if (_settingsButton?.Flyout?.IsOpen == true)
            return true;

        if (DataContext is PlayerViewModel vm && vm.IsScrubbing)
            return true;

        return false;
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        if (!_controlsVisible)
        {
            _hideTimer.Stop();
            return;
        }

        if (DataContext is not PlayerViewModel vm)
        {
            _hideTimer.Stop();
            return;
        }

        if (!vm.AutoHideControls)
        {
            _hideTimer.Stop();
            return;
        }

        if (!vm.IsPlaying && !string.IsNullOrEmpty(vm.VideoUrl))
            return;

        if (vm.IsBufferingOrLoading || vm.Overlay.IsOsdVisible)
            return;
        
        if (IsPointerOverUI())
            return;

        _hideTimer.Stop();
        HideControls();
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        var currentPosition = e.GetPosition(this);
        _lastPointerPosition = currentPosition;

        if (!_controlsVisible)
        {
            if (_hiddenPointerPosition is null)
            {
                _hiddenPointerPosition = currentPosition;
                return;
            }

            var deltaX = currentPosition.X - _hiddenPointerPosition.Value.X;
            var deltaY = currentPosition.Y - _hiddenPointerPosition.Value.Y;
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);

            if (distanceSquared < ShowControlsMovementThresholdSquared)
                return;

            ShowControls();
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastControlsKeepAliveUtc < ControlsKeepAliveInterval)
            return;
            
        _lastControlsKeepAliveUtc = now;

        // Fast-path: if controls are already visible, just restart the timer
        ResetHideTimer();
    }

    private void OnGridPointerExited(object? sender, PointerEventArgs e)
    {
        // When the pointer leaves the grid (e.g., moving outside the window or into a flyout popup), 
        // we do not hide the controls instantly. We rely on the hide timer to expire and hide them 
        // naturally via timeout, ensuring that hovering over flyouts doesn't dismiss the UI.
        if (!_controlsVisible)
        {
            _hiddenPointerPosition = null;
        }
    }
}
