using System.Collections.Specialized;
using Serilog;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    private Debouncer? _collectionChangeDebouncer;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncOrchestrator.IsSyncing || _animeRepo.IsInitializing)
        {
            // During mass sync/init, we'll trigger one big update at the end 
            // instead of hundreds of individual UI refreshes.
            return;
        }

        Log.Debug("AnimeListViewModel: CollectionChanged action={Action}", e.Action);

        // Coalesce bursts: airing/Shikimori sync can fire 30+ Add events in a
        // few hundred ms. The debouncer collapses them into a single refresh.
        _listProjection.ApplyCollectionChange(e, _animeRepo.Collection);
        _collectionChangeDebouncer?.Invoke();
    }
}
