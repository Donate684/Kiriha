using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kiriha.Models;

public partial class AnimeItem
{
    private string _fallbackSeason = string.Empty;

    [NotMapped]
    [JsonIgnore]
    public AnimeItemPresentation Presentation => new(this);

    [NotMapped]
    public string Season
    {
        get
        {
            if (!string.IsNullOrEmpty(StartSeason) && StartYear.HasValue)
                return $"{StartSeason} {StartYear}";
            if (!string.IsNullOrEmpty(StartSeason))
                return StartSeason;
            if (StartYear.HasValue)
                return StartYear.ToString()!;
            return _fallbackSeason;
        }
        set
        {
            _fallbackSeason = value ?? string.Empty;
            OnPropertyChanged(nameof(Season));
        }
    }
}
