using Kiriha.Core.Repositories;
using Kiriha.Services.Data.Repository;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Kiriha.ViewModels.History;

public partial class HistoryViewModel
{
    [RelayCommand]
    public async Task RefreshHistory()
    {
        await _dbInit.InitializationTask;

        _rawItems = await _historyService.GetHistoryAsync();

        // Resolve posters from the local user collection (cheap dictionary lookup).
        try
        {
            var collection = _animeRepo.Collection;
            var posterMap = collection
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var item in _rawItems)
            {
                if (posterMap.TryGetValue(item.AnimeId, out var anime))
                {
                    item.PosterUrl = anime.MainPictureUrl;
                }
            }
        }
        catch { /* Kiriha.Core.Repositories.IAnimeRepository may not be ready at very early startup */ }

        HasHistory = _rawItems.Count > 0;
        ApplyFilters();
    }
}
