
using Avalonia.Controls;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Services.Data;

namespace Kiriha.Mpv.UI.Views.Player;

public partial class PlayerSelectionWindow : Window
{
    private readonly ISettingsService? _settingsService;

    public PlayerSelectionWindow()
    {
        InitializeComponent();
    }

    public PlayerSelectionWindow(ISettingsService settingsService) : this()
    {
        _settingsService = settingsService;
        ApplyMica();
    }

    public void ApplyMica()
    {
        var settings = _settingsService?.Current;
        if (settings is null) return;
        if (settings.UI.EnableMica)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur];
            Background = null;
        }
        else
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            ClearValue(BackgroundProperty);
        }
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
}
