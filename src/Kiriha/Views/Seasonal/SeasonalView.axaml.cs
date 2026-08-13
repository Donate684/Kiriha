using Kiriha.Core.Abstractions.Models.Entities;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.ViewModels.Seasonal;
using Kiriha.Views.Seasonal;

namespace Kiriha.Views.Seasonal;

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
        if (e.Element.DataContext is AnimeEntity item && DataContext is SeasonalViewModel vm)
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
            if (element != null && element.DataContext is AnimeEntity item)
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


}
