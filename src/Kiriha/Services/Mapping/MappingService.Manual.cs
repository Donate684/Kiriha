namespace Kiriha.Services.Data.Mapping;

public partial class MappingService
{
    public void AddMapping(string title, int malId)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        var (cleanTitle, searchTitle, _, _) = ParseAnimeTitle(title);

        string target = !string.IsNullOrWhiteSpace(searchTitle) && searchTitle != cleanTitle
            ? searchTitle
            : (!string.IsNullOrWhiteSpace(cleanTitle) ? cleanTitle : title);

        if (!string.Equals(title, target, StringComparison.OrdinalIgnoreCase))
        {
            _manualMapping.RemoveMapping(title);
        }

        _manualMapping.AddMapping(target, malId);
        ClearRecognitionCaches();
    }

    public void RemoveMapping(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        _manualMapping.RemoveMapping(title);

        var (cleanTitle, searchTitle, _, _) = ParseAnimeTitle(title);
        if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.Equals(cleanTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            _manualMapping.RemoveMapping(cleanTitle);
        }
        if (!string.IsNullOrWhiteSpace(searchTitle) &&
            !string.Equals(searchTitle, title, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(searchTitle, cleanTitle, StringComparison.OrdinalIgnoreCase))
        {
            _manualMapping.RemoveMapping(searchTitle);
        }

        // Clear session cache to force a re-evaluation
        ClearRecognitionCaches();
    }

    public virtual bool IsManuallyMapped(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        string normOriginal = Normalize(title);
        if (_manualMapping.TryGetMapping(normOriginal, out _)) return true;

        var (cleanTitle, searchTitle, _, _) = ParseAnimeTitle(title);

        string normClean = Normalize(cleanTitle);
        if (_manualMapping.TryGetMapping(normClean, out _)) return true;

        string normSearch = Normalize(searchTitle);
        if (normSearch != normClean && _manualMapping.TryGetMapping(normSearch, out _)) return true;

        return false;
    }

    public virtual bool IsNegativelyMapped(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        string normOriginal = Normalize(title);
        if (_manualMapping.IsNegativelyMapped(normOriginal)) return true;

        var (cleanTitle, searchTitle, _, _) = ParseAnimeTitle(title);

        string normClean = Normalize(cleanTitle);
        if (_manualMapping.IsNegativelyMapped(normClean)) return true;

        string normSearch = Normalize(searchTitle);
        if (normSearch != normClean && _manualMapping.IsNegativelyMapped(normSearch)) return true;

        return false;
    }

    public void AddNegativeMapping(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        var (cleanTitle, searchTitle, _, _) = ParseAnimeTitle(title);

        _manualMapping.RemoveMapping(title);

        if (!string.IsNullOrWhiteSpace(searchTitle))
            _manualMapping.AddNegativeMapping(searchTitle);

        if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.Equals(cleanTitle, searchTitle, StringComparison.OrdinalIgnoreCase))
            _manualMapping.AddNegativeMapping(cleanTitle);

        if (string.IsNullOrWhiteSpace(searchTitle) && string.IsNullOrWhiteSpace(cleanTitle))
            _manualMapping.AddNegativeMapping(title);

        ClearRecognitionCaches();
    }
}
