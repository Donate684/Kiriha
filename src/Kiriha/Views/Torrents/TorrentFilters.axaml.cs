using Avalonia.Controls;
using Kiriha.ViewModels.Torrents;

namespace Kiriha.Views.Torrents;

public partial class TorrentFilters : UserControl
{
    public TorrentFilters()
    {
        InitializeComponent();
    }

    private void OnFilterFlyoutOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is TorrentsViewModel vm)
        {
            vm.SyncFilterContext();
        }
    }
}
