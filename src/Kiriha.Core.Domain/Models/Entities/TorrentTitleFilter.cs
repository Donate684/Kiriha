using System;

namespace Kiriha.Core.Domain.Models.Entities;

public class TorrentTitleFilter
{
    public int AnimeId { get; set; }
    public bool OnlyCrunchyroll { get; set; }
    public bool FilterNetflix { get; set; }
    public bool FilterAmazon { get; set; }
    public bool FilterHidive { get; set; }
    public bool FilterVaryg { get; set; }
    public bool FilterEraiRaws { get; set; }
    public bool FilterToonsHub { get; set; }
    public bool FilterJudas { get; set; }
    public bool FilterHevc { get; set; }
    public bool Filter1080p { get; set; }
    public bool UseCustomQuery { get; set; }
    public string? CustomQuery { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
