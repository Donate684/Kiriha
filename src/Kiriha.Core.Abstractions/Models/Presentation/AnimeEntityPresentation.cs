using System;
using Kiriha.Core.Abstractions.Models.Entities;

namespace Kiriha.Core.Abstractions.Models.Entities;

public delegate string GetLocDelegate(string key, params object[] args);

public readonly partial struct AnimeEntityPresentation
{
    public static GetLocDelegate GetLoc { get; set; } = (k, args) => k;

    private readonly AnimeEntity _item;
    private readonly DateTime _now;

    public AnimeEntityPresentation(AnimeEntity item) : this(item, DateTime.UtcNow)
    {
    }

    public AnimeEntityPresentation(AnimeEntity item, DateTime now)
    {
        _item = item;
        _now = now;
    }

    public string DisplayTitle => !string.IsNullOrEmpty(_item.RussianTitle) ? _item.RussianTitle : _item.Title;

    public string? DisplaySynopsis => !string.IsNullOrEmpty(_item.RussianSynopsis) ? _item.RussianSynopsis : _item.Synopsis;

    public bool IsAnime => _item.MediaKind == MediaKind.Anime;
    public bool IsManga => _item.MediaKind != MediaKind.Anime;
}
