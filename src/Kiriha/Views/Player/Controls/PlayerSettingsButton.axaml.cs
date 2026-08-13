using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kiriha.Views.Player.Controls;

public partial class PlayerSettingsButton : UserControl
{
    public event EventHandler? SettingsClicked;

    public PlayerSettingsButton()
    {
        InitializeComponent();
    }

    private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SettingsClicked?.Invoke(this, EventArgs.Empty);
    }
}
