using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.ViewModels.Player;

namespace Kiriha.Views.Player;

public partial class PlayerWindow
{
    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Slider slider || !e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed)
            return;

        if (DataContext is PlayerViewModel vm)
            vm.BeginScrub();

        if (slider.Bounds.Width > 0)
        {
            var point = e.GetCurrentPoint(slider).Position;
            var ratio = Math.Clamp(point.X / slider.Bounds.Width, 0, 1);
            slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        }
    }

    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is PlayerViewModel { IsScrubbing: true } vm)
            vm.EndScrub();
    }

    // Background click / drag

    private void OnBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsSettingsOverlayVisible())
        {
            e.Handled = true;
            HideSettingsOverlay();
            return;
        }

        if (DataContext is not PlayerViewModel vm)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        var action = PlayerMouseAction.None;
        if (properties.IsLeftButtonPressed)
            action = vm.LeftClickAction?.Value ?? PlayerMouseAction.TogglePlayPause;
        else if (properties.IsRightButtonPressed)
            action = vm.RightClickAction?.Value ?? PlayerMouseAction.OpenSettings;
        else if (properties.IsMiddleButtonPressed)
            action = vm.MiddleClickAction?.Value ?? PlayerMouseAction.ToggleFullscreen;

        if (action == PlayerMouseAction.None)
            return;

        e.Handled = true;

        if (properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 1)
                ExecuteMouseAction(vm, action);
        }
        else
        {
            ExecuteMouseAction(vm, action);
        }
    }

    private void OnBackgroundDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsSettingsOverlayVisible())
        {
            e.Handled = true;
            HideSettingsOverlay();
            return;
        }

        if (DataContext is not PlayerViewModel vm)
            return;

        var leftAction = vm.LeftClickAction?.Value ?? PlayerMouseAction.TogglePlayPause;
        var doubleClickAction = PlayerMouseAction.ToggleFullscreen;

        if (leftAction == PlayerMouseAction.TogglePlayPause && doubleClickAction != PlayerMouseAction.None)
        {
            ExecuteMouseAction(vm, PlayerMouseAction.TogglePlayPause);
        }

        e.Handled = true;
        ExecuteMouseAction(vm, doubleClickAction);
    }

    private void OnBackgroundPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsSettingsOverlayVisible() || DataContext is not PlayerViewModel vm)
            return;

        var action = e.Delta.Y > 0
            ? vm.WheelUpAction?.Value ?? PlayerWheelAction.VolumeUp
            : vm.WheelDownAction?.Value ?? PlayerWheelAction.VolumeDown;

        if (action == PlayerWheelAction.None)
            return;

        e.Handled = true;
        ExecuteWheelAction(vm, action);
        ShowControls();
    }

    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        BeginOwnerMoveDrag(e);
    }

    private void OnBottomBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsSettingsOverlayVisible())
            return;

        if (IsPlayerControlSource(e.Source))
            return;

        e.Handled = true;
        BeginOwnerMoveDrag(e);
    }

    private void OnDragStripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsSettingsOverlayVisible())
            return;

        e.Handled = true;
        BeginOwnerMoveDrag(e);
    }

    private void BeginOwnerMoveDrag(PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void ExecuteMouseAction(PlayerViewModel vm, PlayerMouseAction action)
    {
        switch (action)
        {
            case PlayerMouseAction.TogglePlayPause:
                vm.TogglePlayPauseCommand.Execute(null);
                break;
            case PlayerMouseAction.ToggleFullscreen:
                OnFullscreenClick(null, new RoutedEventArgs());
                break;
            case PlayerMouseAction.ShowControls:
                ShowControls();
                break;
            case PlayerMouseAction.OpenSettings:
                ShowSettingsOverlay();
                break;
            case PlayerMouseAction.SeekBackward10:
                vm.SkipCommand.Execute("-10");
                break;
            case PlayerMouseAction.SeekForward10:
                vm.SkipCommand.Execute("10");
                break;
            case PlayerMouseAction.CycleAudio:
                vm.CycleAudioCommand.Execute(null);
                break;
            case PlayerMouseAction.CycleSubtitle:
                vm.CycleSubtitleCommand.Execute(null);
                break;
            case PlayerMouseAction.None:
            default:
                break;
        }
    }

    private void ExecuteWheelAction(PlayerViewModel vm, PlayerWheelAction action)
    {
        var step = Math.Max(1, vm.WheelVolumeStep);

        switch (action)
        {
            case PlayerWheelAction.VolumeUp:
                vm.AdjustVolume(step);
                break;
            case PlayerWheelAction.VolumeDown:
                vm.AdjustVolume(-step);
                break;
            case PlayerWheelAction.SeekForward:
                vm.SeekRelative(10);
                break;
            case PlayerWheelAction.SeekBackward:
                vm.SeekRelative(-10);
                break;
            case PlayerWheelAction.SpeedUp:
                vm.PlaybackSpeed = Math.Clamp(vm.PlaybackSpeed + 0.25, 0.25, 2.0);
                break;
            case PlayerWheelAction.SpeedDown:
                vm.PlaybackSpeed = Math.Clamp(vm.PlaybackSpeed - 0.25, 0.25, 2.0);
                break;
            case PlayerWheelAction.None:
            default:
                break;
        }
    }

    private void OnSkipPromptPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            vm.Overlay.PauseSkipPromptTimer();
        }
    }

    private void OnSkipPromptPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
        {
            vm.Overlay.ResumeSkipPromptTimer();
        }
    }
}
