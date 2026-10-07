namespace Kiriha.Core.Domain.Models.Entities;

public class AnimeCountryOrigin
{
    public int MalId { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string FetchedAt { get; set; } = string.Empty;
}
