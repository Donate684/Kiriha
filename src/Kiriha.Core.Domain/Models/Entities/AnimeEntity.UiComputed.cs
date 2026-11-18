using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kiriha.Core.Domain.Models.Entities;

public partial class AnimeEntity
{
    private string _fallbackSeason = string.Empty;

    internal AnimeEntityPresentation? _presentation;

    [NotMapped]
    [JsonIgnore]
    public bool IsPresentationCreated => _presentation != null;

    [NotMapped]
    [JsonIgnore]
    public AnimeEntityPresentation Presentation => _presentation ??= new AnimeEntityPresentation(this);

    [NotMapped]
    [JsonIgnore]
    public string? SecondaryTitle => Presentation.SecondaryTitle;

    [NotMapped]
    [JsonIgnore]
    public bool HasSecondaryTitle => Presentation.HasSecondaryTitle;

    [NotMapped]
    [JsonIgnore]
    public string? JapaneseDisplayTitle => Presentation.JapaneseTitle;

    [NotMapped]
    [JsonIgnore]
    public bool HasJapaneseTitle => Presentation.HasJapaneseTitle;

    public AnimeEntity()
    {
    }

    protected override void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == "Presentation" || string.IsNullOrEmpty(propertyName))
        {
            _presentation?.RaiseAll();
        }
    }

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
