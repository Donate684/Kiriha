using Avalonia.Controls;
namespace Kiriha.Views.Controls.Variants.NowPlayingCompactSections;
public partial class NowPlayingCompactHeader : UserControl
{
    public NowPlayingCompactHeader()
    {
        InitializeComponent();
    }
    private void ShareRow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ShareMainButton");
        btn?.Flyout?.Hide();
    }
}
