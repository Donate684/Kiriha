using System;
using Kiriha.Models.Entities;

namespace Kiriha.Models;

public readonly partial struct AnimeItemPresentation
{
    private readonly AnimeItem _item;
    private readonly DateTime _now;

    public AnimeItemPresentation(AnimeItem item) : this(item, DateTime.Now)
    {
    }

    public AnimeItemPresentation(AnimeItem item, DateTime now)
    {
        _item = item;
        _now = now;
    }

    public string DisplayTitle => !string.IsNullOrEmpty(_item.RussianTitle) ? _item.RussianTitle : _item.Title;

    public string? DisplaySynopsis => !string.IsNullOrEmpty(_item.RussianSynopsis) ? _item.RussianSynopsis : _item.Synopsis;

    public bool IsAnime => _item.MediaKind == MediaKind.Anime;
    public bool IsManga => _item.MediaKind != MediaKind.Anime;
}
