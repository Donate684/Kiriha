using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Kiriha.Mpv.UI.ViewModels.Player;

namespace Kiriha.Views.Player.Controls.Settings
{
    public partial class PlayerAdvancedSettings : UserControl
    {
        public PlayerAdvancedSettings()
        {
            InitializeComponent();
        }

        private async void OnChooseScreenshotDirectoryClick(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is not PlayerViewModel vm)
                return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "ÐŸÐ°Ð¿ÐºÐ° Ð´Ð»Ñ ÑÐºÑ€Ð¸Ð½ÑˆÐ¾Ñ‚Ð¾Ð²",
                AllowMultiple = false
            });

            var folder = folders.FirstOrDefault();
            var path = folder?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
                vm.ScreenshotDirectory = path;
        }
    }
}
