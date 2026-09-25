using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kiriha.Core.Domain.Models.Entities;

public partial class AnimeEntity
{
    private bool _isHiddenInSeasons;

    /// <summary>
    /// Client-only flag mirrored from AppSettings.UI.HiddenSeasonalIds.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public bool IsHiddenInSeasons
    {
        get => _isHiddenInSeasons;
        set => SetProperty(ref _isHiddenInSeasons, value);
    }

    private bool _isHideConfirming;

    /// <summary>
    /// Transient Seasonal view state for the hide-button confirmation.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public bool IsHideConfirming
    {
        get => _isHideConfirming;
        set => SetProperty(ref _isHideConfirming, value);
    }

    private FranchiseContext? _franchise;

    /// <summary>
    /// Transient franchise relation context with the user's library.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public FranchiseContext? Franchise
    {
        get => _franchise;
        set
        {
            if (SetProperty(ref _franchise, value))
            {
                OnPropertyChanged("Presentation");
            }
        }
    }
}
