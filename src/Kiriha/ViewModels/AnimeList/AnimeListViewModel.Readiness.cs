using Avalonia.Threading;
using Kiriha.Services.AppLifecycle;
using Serilog;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    private void OnReadinessStateChanged(object? sender, AppReadinessState state)
    {
        Dispatcher.UIThread.Post(() => IsBusy = state == AppReadinessState.Starting);
    }

    private async Task ObserveReadinessAsync()
    {
        if (_animeRepo.Collection.Count == 0)
        {
            IsSyncing = true;
        }
        IsBusy = _readinessService.State is AppReadinessState.NotStarted or AppReadinessState.Starting;
        try
        {
            await _readinessService.ReadyTask;
            RebuildListProjection();
            await UpdateCountsAsync();
            await ApplyCurrentFiltersAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeListViewModel: readiness observer failed");
        }
        finally
        {
            IsSyncing = false;
            IsBusy = false;
        }
    }
}
