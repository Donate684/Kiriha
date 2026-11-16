using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Api;

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
        // 2. If missing, look up in current collection via thread-safe FindById.
        // 3. If still missing (e.g. item was deleted from collection), look up in IMetadataRepository.
        try
        {
            var missingPosterItems = new List<Kiriha.Core.Domain.Models.HistoryItem>();

            foreach (var item in _rawItems)
            {
                if (string.IsNullOrEmpty(item.PosterUrl))
                {
                    var match = _animeRepo.FindById(item.AnimeId, item.MediaKind);
                    if (!string.IsNullOrEmpty(match?.MainPictureUrl))
                    {
                        item.PosterUrl = match.MainPictureUrl;
                    }
                    else if (item.AnimeId > 0)
                    {
                        missingPosterItems.Add(item);
                    }
                }
            }

            if (missingPosterItems.Count > 0 && _metadataRepo != null)
            {
                var missingKeys = missingPosterItems
                    .Select(x => new Kiriha.Core.Domain.Models.Entities.MediaKey(x.AnimeId, x.MediaKind))
                    .Distinct()
                    .ToList();
                var metas = await _metadataRepo.GetBatchAsync(missingKeys);
                foreach (var item in missingPosterItems)
                {
                    var key = new Kiriha.Core.Domain.Models.Entities.MediaKey(item.AnimeId, item.MediaKind);
                    if (metas != null && metas.TryGetValue(key, out var meta) && !string.IsNullOrEmpty(meta.PosterUrl))
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
