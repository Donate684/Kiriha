using CommunityToolkit.Mvvm.Input;

namespace Kiriha.ViewModels.History;

public partial class HistoryViewModel
{
    [RelayCommand]
    public async Task RefreshHistory()
    {
        if (_dbInit?.InitializationTask != null)
            await _dbInit.InitializationTask;

        _rawItems = await _historyService.GetHistoryAsync();

        // Resolve posters:
        // 1. If item.PosterUrl is already stored in DB, keep it.
        // 2. If missing, look up in current collection (cheap in-memory map).
        // 3. If still missing (e.g. anime was deleted from collection), look up in IMetadataRepository.
        try
        {
            var collection = _animeRepo.Collection;
            var posterMap = collection
                .Where(x => !string.IsNullOrEmpty(x.MainPictureUrl))
                .DistinctBy(x => x.Id)
                .ToDictionary(x => x.Id, x => x.MainPictureUrl!);

            var missingPosterItems = new List<Kiriha.Core.Domain.Models.HistoryItem>();

            foreach (var item in _rawItems)
            {
                if (string.IsNullOrEmpty(item.PosterUrl))
                {
                    if (posterMap.TryGetValue(item.AnimeId, out var url))
                    {
                        item.PosterUrl = url;
                    }
                    else if (item.AnimeId > 0)
                    {
                        missingPosterItems.Add(item);
                    }
                }
            }

            if (missingPosterItems.Count > 0 && _metadataRepo != null)
            {
                var missingIds = missingPosterItems.Select(x => x.AnimeId).Distinct().ToList();
                var metas = await _metadataRepo.GetBatchAsync(missingIds);
                foreach (var item in missingPosterItems)
                {
                    if (metas.TryGetValue(item.AnimeId, out var meta) && !string.IsNullOrEmpty(meta.PosterUrl))
                    {
                        item.PosterUrl = meta.PosterUrl;
                    }
                }
            }
        }
        catch { /* IAnimeRepository / IMetadataRepository may not be ready at very early startup */ }

        HasHistory = _rawItems.Count > 0;
        ApplyFilters();
    }
}
