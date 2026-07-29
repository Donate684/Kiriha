using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kiriha.Core.Mpv;
using Kiriha.ViewModels.Player;

namespace Kiriha.Views.Player.Controls;

public partial class PlayerControlBar : UserControl
{
    public event EventHandler? SettingsClicked;
    public event EventHandler? ActionExecuted;

    public PlayerControlBar()
    {
        InitializeComponent();
        
        var settingsBtn = this.FindControl<PlayerSettingsButton>("SettingsBtn");
        if (settingsBtn != null)
        {
            settingsBtn.SettingsClicked += (s, e) =>
            {
                SettingsClicked?.Invoke(this, EventArgs.Empty);
            };
        }
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
                ActionExecuted?.Invoke(this, EventArgs.Empty);
                break;
            case PointerUpdateKind.RightButtonReleased:
                e.Handled = true;
                vm.TakeScreenshot(includeSubtitles: true);
                ActionExecuted?.Invoke(this, EventArgs.Empty);
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
                ActionExecuted?.Invoke(this, EventArgs.Empty);
                break;
            case PointerUpdateKind.RightButtonReleased:
                e.Handled = true;
                vm.MoveSubtitleDown();
                ActionExecuted?.Invoke(this, EventArgs.Empty);
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
        ActionExecuted?.Invoke(this, EventArgs.Empty);
    }
}
