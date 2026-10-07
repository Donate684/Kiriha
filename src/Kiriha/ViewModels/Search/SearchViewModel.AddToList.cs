using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Entities;
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
            var result = await _listActionService.AddToListAsync(item, status);
            if (!result.Success && !string.IsNullOrEmpty(result.Message))
            {
                Log.Warning("SearchViewModel: Failed to add {Title}: {Message}", item.Title, result.Message);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add {Title}", item.Title);
        }
        finally { IsLoading = false; }
    }
}
