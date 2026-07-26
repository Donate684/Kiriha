using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Kiriha.ViewModels.Player;

namespace Kiriha.Views.Player.Controls;

public partial class PlayerSettingsOverlay : UserControl
{
    public event EventHandler? OverlayClosed;

    public PlayerSettingsOverlay()
    {
        InitializeComponent();
    }

    public void ShowOverlay()
    {
        IsVisible = true;
        if (DataContext is PlayerViewModel vm)
            vm.SetMpvRuntimeDiagnosticsVisible(true);
    }

    public void HideOverlay()
    {
        IsVisible = false;
        if (DataContext is PlayerViewModel vm)
            vm.SetMpvRuntimeDiagnosticsVisible(false);
        OverlayClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        HideOverlay();
    }

    private void OnSettingsPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnSettingsCloseClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideOverlay();
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
            Title = "Папка для скриншотов",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        var path = folder?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            vm.ScreenshotDirectory = path;
    }
}
