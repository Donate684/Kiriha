using System;

namespace Kiriha.Core.Domain.Models.Entities;

public class HiddenTorrentAnime
{
    public int AnimeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
