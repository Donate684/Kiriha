using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Kiriha.ViewModels.History;

public partial class HistoryViewModel
{
    [RelayCommand]
    public void ClearSearch() => SearchQuery = string.Empty;

    [RelayCommand]
    public async Task OpenAnimeDetails(HistoryEntryVm entry)
    {
        if (entry == null) return;

        var fullItem = _animeRepo.Collection.FirstOrDefault(x => x.Id == entry.AnimeId);
        if (fullItem == null)
            fullItem = await _malApi.GetAnimeDetailsAsync(entry.AnimeId);

        if (fullItem != null)
        {
            await _dialogs.ShowAnimeDetailsAsync(null, fullItem);
            await RefreshHistory();
        }
    }
}
