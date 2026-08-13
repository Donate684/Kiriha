using Kiriha.Services.Data.Settings;
using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.Views.AnimeList;

public partial class AnimeListView : UserControl
{
    private ItemsRepeater? _gridRepeater;

    // Style switching removed. Only Floating Magazine is used.

    public AnimeListView()
    {
        InitializeComponent();
        Focusable = true;
        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);

        _gridRepeater = this.FindControl<ItemsRepeater>("AnimeGridRepeater");
        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared += OnGridElementPrepared;
            _gridRepeater.ElementClearing += OnGridElementClearing;
        }

        var map = this.FindControl<AnimeReleaseMapView>("ReleaseMapView");
        if (map != null)
        {
            map.IsMapVisibleChanged += (s, isVisible) =>
            {
                var scroll = this.FindControl<ScrollViewer>("AnimeListScrollViewer");
                var tabs = this.FindControl<Border>("StatusTabsPanel");
                if (scroll != null) scroll.IsVisible = !isVisible;
                if (tabs != null) tabs.IsVisible = !isVisible;

                // Also notify header
                var header = this.FindControl<AnimeListHeader>("AnimeListHeader");
                if (header != null)
                {
                    header.SetReleaseMapButtonState(isVisible);
                }
            };
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is not AnimeListViewModel vm) return;
        var settings = vm.SettingsService;

        // Ensure ItemsRepeater uses the Poster First template
        if (_gridRepeater != null && this.TryFindResource("CardTemplatePosterFirst", this.ActualThemeVariant, out var resource) && resource is IDataTemplate dt)
        {
            _gridRepeater.ItemTemplate = dt;
            if (_gridRepeater.Layout is Avalonia.Layout.UniformGridLayout layout)
            {
                layout.MinItemWidth = 172;
                layout.MinItemHeight = 326;
            }
        }

        BeginInitialRevealWindow();
        // Initial viewport kickstart
        Avalonia.Threading.Dispatcher.UIThread.Post(QueueVisibleItems, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared -= OnGridElementPrepared;
            _gridRepeater.ElementClearing -= OnGridElementClearing;
        }

        if (DataContext is AnimeListViewModel vm && vm.FilteredItems != null)
        {
            // Unloading logic handled by AsyncImageLoader automatically or no longer needed
        }

        base.OnUnloaded(e);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
    }
}
