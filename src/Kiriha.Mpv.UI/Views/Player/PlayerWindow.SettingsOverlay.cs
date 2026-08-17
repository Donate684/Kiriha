using Avalonia.Controls;
using Kiriha.Mpv.UI.Views.Player.Controls;

namespace Kiriha.Mpv.UI.Views.Player;

public partial class PlayerWindow
{
    private void ShowSettingsOverlay()
    {
        this.FindControl<PlayerSettingsOverlay>("SettingsOverlayControl")?.ShowOverlay();
        ShowControls();
    }

    private void HideSettingsOverlay()
    {
        this.FindControl<PlayerSettingsOverlay>("SettingsOverlayControl")?.HideOverlay();
    }

    private bool IsSettingsOverlayVisible()
    {
        return this.FindControl<PlayerSettingsOverlay>("SettingsOverlayControl")?.IsVisible == true;
    }
}
