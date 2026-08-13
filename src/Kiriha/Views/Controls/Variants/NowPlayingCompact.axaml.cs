using Avalonia.Controls;

namespace Kiriha.Views.Controls.Variants;

public partial class NowPlayingCompact : UserControl
{
    public NowPlayingCompact()
    {
        InitializeComponent();
    }

    private void ShareRow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ShareMainButton");
        btn?.Flyout?.Hide();
    }
}
