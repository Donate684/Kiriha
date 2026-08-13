using Kiriha.Services.Data.Settings;
using System;
using Avalonia.Input;
using Kiriha.Core.Utils;
using Kiriha.Services.Data;

namespace Kiriha.Views;

public partial class AnimeDetailsWindow : KirihaWindowBase
{
    public AnimeDetailsWindow()
    {
        InitializeComponent();
    }

    public AnimeDetailsWindow(Kiriha.Core.Services.ISettingsService settingsService) : this()
    {
        SettingsService = settingsService;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        this.CenterOnOwnerOrScreenSafe();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }
}
