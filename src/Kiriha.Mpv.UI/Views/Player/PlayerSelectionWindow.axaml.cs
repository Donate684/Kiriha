
using Avalonia.Controls;
using Kiriha.Services.Data;

namespace Kiriha.Mpv.UI.Views.Player;

public partial class PlayerSelectionWindow : Window
{
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService? _settingsService;

    public PlayerSelectionWindow()
    {
        InitializeComponent();
    }

    public PlayerSelectionWindow(Kiriha.Core.Abstractions.Services.ISettingsService settingsService) : this()
    {
        _settingsService = settingsService;
        ApplyMica();
    }

    public void ApplyMica()
    {
        var settings = _settingsService?.Current;
        if (settings == null) return;
        if (settings.UI.EnableMica)
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur };
            Background = null;
        }
        else
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
            ClearValue(BackgroundProperty);
        }
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
}
