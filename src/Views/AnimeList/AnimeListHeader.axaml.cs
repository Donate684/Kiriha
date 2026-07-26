using Avalonia.Controls;

namespace Kiriha.Views.AnimeList
{
    public partial class AnimeListHeader : UserControl
    {
        public event System.EventHandler? ReleaseMapRequested;

        public AnimeListHeader()
        {
            InitializeComponent();
        }

        private void ReleaseMapButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ReleaseMapRequested?.Invoke(this, System.EventArgs.Empty);
        }

        public void SetReleaseMapButtonState(bool isActive)
        {
            var button = this.FindControl<Button>("ReleaseMapButton");
            if (button != null)
            {
                if (isActive)
                    button.Classes.Add("active");
                else
                    button.Classes.Remove("active");
            }
        }
    }
}
