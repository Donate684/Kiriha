using Kiriha.Core.Domain.Models.Entities;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.Views.AnimeList;

public partial class AnimeListView
{
    // Reveal-on-load runs only for the initially rendered page. Recycled
    // cards that enter during scrolling skip reveal transitions to avoid
    // dozens of parallel Opacity/Transform animations hurting scroll FPS.
    private DateTime _lastRevealEvent = DateTime.MinValue;
    private int _revealStaggerIndex;
    // Start as active because ItemsRepeater may realize initial elements
    // before OnLoaded; BeginInitialRevealWindow restarts the timer there.
    private bool _initialRevealActive = true;
    private const int RevealStaggerStepMs = 45;
    private const int RevealStaggerIdleResetMs = 140;
    private static readonly TimeSpan InitialRevealWindow = TimeSpan.FromMilliseconds(1100);

    /// <summary>
    /// Opens a short window where prepared cards play the reveal cascade.
    /// After it closes, cards entering the viewport during scroll show instantly.
    /// </summary>
    private void BeginInitialRevealWindow()
    {
        _initialRevealActive = true;
        _revealStaggerIndex = 0;
        DispatcherTimer.RunOnce(() => _initialRevealActive = false, InitialRevealWindow);
    }

    /// <summary>
    /// When an item enters the viewport, enqueue it for image download if needed.
    /// This is the core of the lazy loading mechanism.
    /// </summary>
    private void OnGridElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element.DataContext is AnimeEntity item && DataContext is AnimeListViewModel vm)
        {
            vm.EnqueueItemForViewport(item);
        }

        if (e.Element is not Border card || !card.Classes.Contains("revealItem"))
            return;

        // Outside the initial reveal window this is a scroll-entering card.
        // Remove reveal classes so it appears immediately without transitions.
        if (!_initialRevealActive)
        {
            card.Classes.Remove("revealItem");
            card.Classes.Remove("shown");
            return;
        }

        // Reveal-on-load: staggered appearance delay.
        card.Classes.Remove("shown");

        var now = DateTime.UtcNow;
        if ((now - _lastRevealEvent).TotalMilliseconds > RevealStaggerIdleResetMs)
            _revealStaggerIndex = 0;
        _lastRevealEvent = now;

        var delay = TimeSpan.FromMilliseconds(_revealStaggerIndex++ * RevealStaggerStepMs);
        DispatcherTimer.RunOnce(() => card.Classes.Add("shown"), delay);
    }

    /// <summary>
    /// When an item is recycled (scrolls out of view), the ItemsRepeater 
    /// automatically recycles the UI element. We don't null LocalPosterPath:
    /// the path string is tiny, and keeping it allows instant image reload
    /// when scrolling back. Memory is bounded by the ~30 recycled controls.
    /// </summary>
    private void OnGridElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        // Reset reveal state on recycled cards so the next preparation can
        // decide whether to animate them.
        if (e.Element is Border card && card.Classes.Contains("revealItem"))
            card.Classes.Remove("shown");
    }
}
