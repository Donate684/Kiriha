using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    public async Task QuickAddToList(AnimeEntity item, UserAnimeStatus status)
    {
        try
        {
            var result = await _listActionService.AddToListAsync(item, status);
            if (!result.Success && !string.IsNullOrEmpty(result.Message))
            {
                Log.Warning("SeasonalViewModel.QuickAddToList failed for {Title}: {Message}", item.Title, result.Message);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SeasonalViewModel.QuickAddToList failed for {Title}", item.Title);
        }
    }
}
