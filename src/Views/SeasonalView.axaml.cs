using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kiriha.Models;
using Kiriha.ViewModels.Seasonal;
using Kiriha.Views.Seasonal;

namespace Kiriha.Views;

public partial class SeasonalView : UserControl
{
    private ItemsRepeater? _gridRepeater;
    private SeasonalRevealController? _revealController;
    private readonly SeasonalHideConfirmController _hideConfirmController = new();

    public SeasonalView()
    {
        InitializeComponent();
        _gridRepeater = this.FindControl<ItemsRepeater>("SeasonalItemsRepeater");
        if (_gridRepeater != null)
        {
            _revealController = new SeasonalRevealController(_gridRepeater);
            _gridRepeater.ElementPrepared += OnGridElementPrepared;
        }
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _revealController?.BeginInitialRevealWindow();
        Avalonia.Threading.Dispatcher.UIThread.Post(QueueVisibleItems, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared -= OnGridElementPrepared;
        }
        _revealController?.Dispose();
        _hideConfirmController.ResetHideConfirm();
        if (DataContext is SeasonalViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        base.OnUnloaded(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SeasonalViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SeasonalViewModel.DisplayItems))
        {
            _revealController?.BeginInitialRevealWindow();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ContentScrollViewer.Offset = Avalonia.Vector.Zero;
                QueueVisibleItems();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnGridElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element.DataContext is AnimeItem item && DataContext is SeasonalViewModel vm)
        {
            vm.EnqueueItemForViewport(item);
        }
    }

    private void QueueVisibleItems()
    {
        if (_gridRepeater?.ItemsSourceView == null || _gridRepeater.ItemsSourceView.Count == 0) return;

        bool foundAny = false;
        for (int i = 0; i < Math.Min(_gridRepeater.ItemsSourceView.Count, 50); i++)
        {
            var element = _gridRepeater.TryGetElement(i);
            if (element != null && element.DataContext is AnimeItem item)
            {
                if (DataContext is SeasonalViewModel vm)
                {
                    vm.EnqueueItemForViewport(item);
                    foundAny = true;
                }
            }
        }

        if (!foundAny && _gridRepeater.ItemsSourceView.Count > 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                QueueVisibleItems();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private async void Poster_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        try
        {
            if (sender is Control c && c.DataContext is AnimeItem item)
            {
                if (DataContext is SeasonalViewModel vm)
                {
                    await vm.DialogService.ShowAnimeDetailsAsync(this, item);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalView.Poster_DoubleTapped failed");
        }
    }

    private void HideBtn_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is SeasonalViewModel vm)
        {
            _hideConfirmController.HandleHideBtnTapped(sender, e, vm);
        }
    }

    private void QuickAddBtn_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (sender is Control c && c.ContextFlyout != null)
            {
                c.ContextFlyout.ShowAt(c);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalView.QuickAddBtn_Tapped failed");
        }
    }

    private void QuickAddMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuItem menuItem &&
                menuItem.Tag is string statusStr && Enum.TryParse<Models.Entities.UserAnimeStatus>(statusStr, out var status) &&
                menuItem.DataContext is AnimeItem item &&
                DataContext is SeasonalViewModel vm)
            {
                _ = vm.QuickAddToList(item, status);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalView.QuickAddMenuItem_Click failed");
        }
    }
}
