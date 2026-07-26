using Avalonia.Markup.Xaml;

namespace Kiriha.Views.Torrents;

public partial class TorrentTemplates : Avalonia.Controls.ResourceDictionary
{
    public TorrentTemplates()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
