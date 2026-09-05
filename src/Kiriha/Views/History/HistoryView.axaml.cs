using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kiriha.ViewModels.History;

namespace Kiriha.Views.History;

public partial class HistoryView : UserControl
{
    private ItemsRepeater? _historyRepeater;
    private DateTime _lastRevealEvent = DateTime.MinValue;
    private int _revealStaggerIndex;
    private bool _initialRevealActive = true;
    private const int BaseRevealDelayMs = 35;
    private const int RevealStaggerStepMs = 45;
    private const int RevealStaggerIdleResetMs = 140;
    private static readonly TimeSpan InitialRevealWindow = TimeSpan.FromMilliseconds(1100);

    public HistoryView()
    {
        InitializeComponent();

        _historyRepeater = this.FindControl<ItemsRepeater>("HistoryRepeater");
        if (_historyRepeater != null)
        {
            _historyRepeater.ElementPrepared += OnElementPrepared;
            _historyRepeater.ElementClearing += OnElementClearing;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BeginInitialRevealWindow();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_historyRepeater != null)
        {
            _historyRepeater.ElementPrepared -= OnElementPrepared;
            _historyRepeater.ElementClearing -= OnElementClearing;
        }

        base.OnUnloaded(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is HistoryViewModel vm)
        {
            vm.TimelineItems.CollectionChanged += (s, args) =>
            {
                BeginInitialRevealWindow();
            };
        }
    }

    public void BeginInitialRevealWindow()
    {
        _initialRevealActive = true;
        _revealStaggerIndex = 0;
        DispatcherTimer.RunOnce(() => _initialRevealActive = false, InitialRevealWindow);
    }

    private void OnElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        var card = FindRevealCard(e.Element);
        if (card == null) return;

        if (!_initialRevealActive)
        {
            card.Classes.Remove("revealItem");
            card.Classes.Remove("shown");
            return;
        }

        if (!card.Classes.Contains("revealItem"))
            card.Classes.Add("revealItem");

        card.Classes.Remove("shown");

        var now = DateTime.UtcNow;
        if ((now - _lastRevealEvent).TotalMilliseconds > RevealStaggerIdleResetMs)
            _revealStaggerIndex = 0;
        _lastRevealEvent = now;

        var delay = TimeSpan.FromMilliseconds(BaseRevealDelayMs + _revealStaggerIndex++ * RevealStaggerStepMs);
        DispatcherTimer.RunOnce(() => card.Classes.Add("shown"), delay);
    }

    private void OnElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        var card = FindRevealCard(e.Element);
        if (card != null && card.Classes.Contains("revealItem"))
        {
            card.Classes.Remove("shown");
        }
    }

    private static Border? FindRevealCard(Control? element)
    {
        if (element is Border b && b.Classes.Contains("revealItem"))
            return b;

        if (element is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is Border card && card.Classes.Contains("revealItem"))
                    return card;
            }
        }

        return null;
    }

    private void HistoryItem_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is HistoryEntryVm entry)
        {
            if (DataContext is HistoryViewModel vm)
            {
                vm.OpenAnimeDetailsCommand.Execute(entry);
            }
        }
    }
}
