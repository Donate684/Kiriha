using System;
using Avalonia.Controls;

namespace Kiriha.Views.Seasonal;

public class SeasonalRevealController
{
    private DateTime _lastRevealEvent = DateTime.MinValue;
    private int _revealStaggerIndex;
    private bool _initialRevealActive = true;
    private const int BaseRevealDelayMs = 35;
    private const int RevealStaggerStepMs = 45;
    private const int RevealStaggerIdleResetMs = 140;
    private static readonly TimeSpan InitialRevealWindow = TimeSpan.FromMilliseconds(1100);
    
    private readonly ItemsRepeater _gridRepeater;

    public SeasonalRevealController(ItemsRepeater gridRepeater)
    {
        _gridRepeater = gridRepeater;
        _gridRepeater.ElementPrepared += OnGridElementPrepared;
        _gridRepeater.ElementClearing += OnGridElementClearing;
    }
    
    public void Dispose()
    {
        _gridRepeater.ElementPrepared -= OnGridElementPrepared;
        _gridRepeater.ElementClearing -= OnGridElementClearing;
    }

    public void BeginInitialRevealWindow()
    {
        _initialRevealActive = true;
        _revealStaggerIndex = 0;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => _initialRevealActive = false, InitialRevealWindow);
    }

    private void OnGridElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element is not Border card || !card.Classes.Contains("revealItem"))
            return;

        if (!_initialRevealActive)
        {
            card.Classes.Remove("revealItem");
            card.Classes.Remove("shown");
            return;
        }

        card.Classes.Remove("shown");

        var now = DateTime.UtcNow;
        if ((now - _lastRevealEvent).TotalMilliseconds > RevealStaggerIdleResetMs)
            _revealStaggerIndex = 0;
        _lastRevealEvent = now;

        var delay = TimeSpan.FromMilliseconds(BaseRevealDelayMs + _revealStaggerIndex++ * RevealStaggerStepMs);
        Avalonia.Threading.DispatcherTimer.RunOnce(() => card.Classes.Add("shown"), delay);
    }

    private void OnGridElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        if (e.Element is Border card && card.Classes.Contains("revealItem"))
            card.Classes.Remove("shown");
    }
}
