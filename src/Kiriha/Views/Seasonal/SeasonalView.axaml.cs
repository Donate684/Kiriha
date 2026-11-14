using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.ViewModels.Seasonal;

namespace Kiriha.Views.Seasonal;

public partial class SeasonalView : UserControl
{
    private const int MaxQueueVisibleRetries = 15;
    private ItemsRepeater? _gridRepeater;
    private SeasonalRevealController? _revealController;
    private readonly SeasonalHideConfirmController _hideConfirmController = new();
    private readonly HashSet<AnimeEntity> _pendingViewportItems = new();
    private DispatcherTimer? _viewportDebounceTimer;
    private SeasonalViewModel? _subscribedVm;
    private bool _isLoaded;
    private int _queueVisibleRetries;

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
        _isLoaded = true;
        _queueVisibleRetries = 0;

        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared -= OnGridElementPrepared;
            _gridRepeater.ElementPrepared += OnGridElementPrepared;
            _revealController ??= new SeasonalRevealController(_gridRepeater);
        }

        UpdateViewModelSubscription(DataContext as SeasonalViewModel);

        _revealController?.BeginInitialRevealWindow();
        Avalonia.Threading.Dispatcher.UIThread.Post(QueueVisibleItems, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _isLoaded = false;
        _queueVisibleRetries = 0;
        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared -= OnGridElementPrepared;
        }
        _revealController?.Dispose();
        _revealController = null;
        _hideConfirmController.ResetHideConfirm();
        _viewportDebounceTimer?.Stop();
        lock (_pendingViewportItems)
        {
            _pendingViewportItems.Clear();
        }
        UpdateViewModelSubscription(null);
        base.OnUnloaded(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UpdateViewModelSubscription(_isLoaded ? DataContext as SeasonalViewModel : null);
    }

    private void UpdateViewModelSubscription(SeasonalViewModel? nextVm)
    {
        if (ReferenceEquals(_subscribedVm, nextVm)) return;
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _subscribedVm = nextVm;
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_isLoaded) return;
        if (e.PropertyName == nameof(SeasonalViewModel.DisplayItems))
        {
            _queueVisibleRetries = 0;
            _revealController?.BeginInitialRevealWindow();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!_isLoaded) return;
                ContentScrollViewer.Offset = Avalonia.Vector.Zero;
                QueueVisibleItems();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void EnqueueViewportItemDebounced(AnimeEntity item)
    {
        lock (_pendingViewportItems)
        {
            _pendingViewportItems.Add(item);
        }

        if (_viewportDebounceTimer is null)
        {
            _viewportDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _viewportDebounceTimer.Tick += (_, _) => FlushViewportQueue();
        }

        _viewportDebounceTimer.Stop();
        _viewportDebounceTimer.Start();
    }

    private void FlushViewportQueue()
    {
        _viewportDebounceTimer?.Stop();
        List<AnimeEntity> batch;
        lock (_pendingViewportItems)
        {
            if (_pendingViewportItems.Count == 0) return;
            batch = new List<AnimeEntity>(_pendingViewportItems);
            _pendingViewportItems.Clear();
        }

        if (DataContext is SeasonalViewModel vm)
        {
            vm.EnqueueItemsForViewport(batch);
        }
    }

    private void OnGridElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element.DataContext is AnimeEntity item)
        {
            EnqueueViewportItemDebounced(item);
        }
    }

    private void QueueVisibleItems()
    {
        if (!_isLoaded || _gridRepeater?.ItemsSourceView is null || _gridRepeater.ItemsSourceView.Count == 0) return;

        var items = new List<AnimeEntity>();
        for (int i = 0; i < Math.Min(_gridRepeater.ItemsSourceView.Count, 50); i++)
        {
            var element = _gridRepeater.TryGetElement(i);
            if (element != null && element.DataContext is AnimeEntity item)
            {
                items.Add(item);
            }
        }

        if (items.Count > 0 && DataContext is SeasonalViewModel vm)
        {
            _queueVisibleRetries = 0;
            vm.EnqueueItemsForViewport(items);
        }
        else if (items.Count == 0 && _gridRepeater.ItemsSourceView.Count > 0 && _queueVisibleRetries < MaxQueueVisibleRetries)
        {
            _queueVisibleRetries++;
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                if (_isLoaded)
                {
                    QueueVisibleItems();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void SortListBox_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is SeasonalViewModel vm)
        {
            vm.ApplyFilters();
        }
    }
}
