using System.IO;
using System.Linq;

namespace Kiriha.Services.Data;

public partial class MappingService
{
    public void AddMapping(string title, int malId)
    {
        _manualMapping.AddMapping(title, malId);
        ClearRecognitionCaches();
    }

    public void RemoveMapping(string title)
    {
        _manualMapping.RemoveMapping(title);
        // Clear session cache to force a re-evaluation
        ClearRecognitionCaches();
    }

    public virtual bool IsManuallyMapped(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        string normOriginal = Normalize(title);
        if (_manualMapping.TryGetMapping(normOriginal, out _)) return true;

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
        var titleElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
        string cleanTitle = titleElement != null ? titleElement.Value : Path.GetFileNameWithoutExtension(title);

        string normClean = Normalize(cleanTitle);
        if (_manualMapping.TryGetMapping(normClean, out _)) return true;

        return false;
    }

    public virtual bool IsNegativelyMapped(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (_manualMapping.IsNegativelyMapped(Normalize(title))) return true;

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
        var titleElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
        string cleanTitle = titleElement != null ? titleElement.Value : Path.GetFileNameWithoutExtension(title);
        if (_manualMapping.IsNegativelyMapped(Normalize(cleanTitle))) return true;

        return false;
    }

    public void AddNegativeMapping(string title)
    {
        _manualMapping.AddNegativeMapping(title);
        ClearRecognitionCaches();
    }
}
