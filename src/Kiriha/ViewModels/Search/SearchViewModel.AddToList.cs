using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Core.Messages;
using Serilog;

namespace Kiriha.ViewModels.Search;

public partial class SearchViewModel
{
    [RelayCommand]
    public async Task AddToWatching(AnimeEntity item) => await AddToList(item, UserAnimeStatus.Watching);

    [RelayCommand]
    public async Task AddToPlanToWatch(AnimeEntity item) => await AddToList(item, UserAnimeStatus.PlanToWatch);

    private async Task AddToList(AnimeEntity item, UserAnimeStatus status)
    {
        IsLoading = true;
        try
        {
            item.Status = status;
            await _animeRepo.AddOrUpdateAnimeAsync(item);
            await _syncManager.EnqueueUpdateAsync(item.Id, 0, status);

            // Notify UI
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add {Title}", item.Title);
        }
        finally { IsLoading = false; }
    }
}
