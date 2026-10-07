using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncMalCommand))]
    private bool _isSyncing;

    private bool CanSync() => !IsSyncing && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSync))]
    public async Task SyncMal()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        IsBusy = true;
        try
        {
            bool success = SelectedMediaKind == MediaKind.Manga || SelectedMediaKind == MediaKind.LightNovel
                ? await _refreshService.RefreshMangaListAsync()
                : await _refreshService.RefreshAnimeListAsync();

            if (success)
            {
                RebuildListProjection();
                await UpdateCountsAsync();
                await ApplyCurrentFiltersAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual sync failed");
        }
        finally
        {
            IsSyncing = false;
            IsBusy = false;
        }
    }
}
