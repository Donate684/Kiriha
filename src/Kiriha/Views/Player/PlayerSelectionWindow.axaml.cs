using Avalonia.Input;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Views.Player;

public partial class PlayerSelectionWindow : KirihaWindowBase
{
    public PlayerSelectionWindow()
    {
        InitializeComponent();
    }

    public PlayerSelectionWindow(ISettingsService settingsService) : this()
    {
        SettingsService = settingsService;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
}
