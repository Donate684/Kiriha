using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kiriha.Models;
using Kiriha.ViewModels.AnimeList;
using System;

namespace Kiriha.Views.AnimeList;

public partial class AnimeListView
{
    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12)
        {
            this.FindControl<AnimeReleaseMapView>("ReleaseMapView")?.ToggleReleaseMap();
            e.Handled = true;
            return;
        }
    }

    private void AnimeListHeader_ReleaseMapRequested(object? sender, EventArgs e)
    {
        this.FindControl<AnimeReleaseMapView>("ReleaseMapView")?.ToggleReleaseMap();
    }
    
    private void QueueVisibleItems()
    {
        if (_gridRepeater?.ItemsSourceView == null) return;

        // Iterate through currently realized elements to ensure they are queued
        // This handles the case where items were prepared before OnLoaded or events were attached.
        for (int i = 0; i < _gridRepeater.ItemsSourceView.Count; i++)
        {
            var element = _gridRepeater.TryGetElement(i);
            if (element != null && element.DataContext is AnimeItem item)
            {
                if (DataContext is AnimeListViewModel vm)
                {
                    vm.EnqueueItemForViewport(item);
                }
            }
        }
    }
}
