using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Models;
using Kiriha.ViewModels.Settings;
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

    private void OnSourcesFlyoutOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is TorrentsViewModel vm)
        {
            vm.RefreshAnimeLibAuthStatus();
            vm.RefreshAnime365AuthStatus();
        }
    }

    private void OnAnimeLibSettingsLinkClick(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as TorrentsViewModel;
        if (vm == null && sender is Visual v)
        {
            vm = v.DataContext as TorrentsViewModel;
        }

        if (vm != null)
        {
            vm.NavigateToStreamingSettings();
        }
        else
        {
            WeakReferenceMessenger.Default.Send(
                new NavigationMessage(Kiriha.Models.NavigationPage.Settings, SettingsViewModel.StreamingSectionIndex));
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SourcesButton?.Flyout?.Hide();
        });
    }
}
