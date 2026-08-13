using Avalonia.Controls;
using Kiriha.Views.Player.Controls;

namespace Kiriha.Views.Player;

public partial class PlayerOverlayWindow
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
