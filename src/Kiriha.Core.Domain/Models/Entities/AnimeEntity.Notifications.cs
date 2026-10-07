namespace Kiriha.Core.Domain.Models.Entities;

public partial class AnimeEntity
{

    public void RefreshMetadata() => OnPropertyChanged(string.Empty);

    /// <summary>
    /// Refreshes only the time-dependent airing badge properties.
    /// </summary>
    public void RefreshAiringBadge()
    {
        OnPropertyChanged(nameof(Presentation));
    }
}
