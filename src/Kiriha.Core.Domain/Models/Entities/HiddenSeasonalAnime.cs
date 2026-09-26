using System;

namespace Kiriha.Core.Domain.Models.Entities;

public class HiddenSeasonalAnime
{
    public int AnimeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
