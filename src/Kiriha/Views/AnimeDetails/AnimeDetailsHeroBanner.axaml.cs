using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kiriha.Views.AnimeDetails
{
    public partial class AnimeDetailsHeroBanner : UserControl
    {
        public AnimeDetailsHeroBanner()
        {
            InitializeComponent();
        }

        private void ShareRow_Click(object? sender, RoutedEventArgs e)
        {
            var btn = this.FindControl<Button>("ShareMainButton");
            btn?.Flyout?.Hide();
        }
    }
}
