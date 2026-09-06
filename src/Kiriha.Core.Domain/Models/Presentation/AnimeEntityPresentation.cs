using System;
using System.ComponentModel;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Models.Entities;

public delegate string GetLocDelegate(string key, params object[] args);

public partial class AnimeEntityPresentation : INotifyPropertyChanged
{
    public static GetLocDelegate GetLoc { get; set; } = (k, args) => k;
    public static Func<bool> GetUseRussianTitles { get; set; } = () => false;

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AnimeEntity _item;
    private DateTime _now;

    public AnimeEntityPresentation(AnimeEntity item)
    {
        _item = item;
        _now = DateTime.UtcNow;
    }

    public AnimeEntityPresentation(AnimeEntity item, DateTime now)
    {
        _item = item;
        _now = now;
    }

    public void RaiseAll()
    {
        _now = DateTime.UtcNow;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string DisplayTitle => !string.IsNullOrEmpty(_item.RussianTitle) ? _item.RussianTitle : _item.Title;

    public string? SecondaryTitle
    {
        get
        {
            var primary = !string.IsNullOrWhiteSpace(_item.Title)
                ? _item.Title
                : (!string.IsNullOrWhiteSpace(_item.EnglishTitle) ? _item.EnglishTitle : _item.RussianTitle);

            if (GetUseRussianTitles())
            {
                if (!string.IsNullOrWhiteSpace(_item.RussianTitle) &&
                    !string.Equals(_item.RussianTitle.Trim(), primary?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return _item.RussianTitle.Trim();
                }

                if (!string.IsNullOrWhiteSpace(_item.EnglishTitle) &&
                    !string.Equals(_item.EnglishTitle.Trim(), primary?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return _item.EnglishTitle.Trim();
                }

                return null;
            }

            if (!string.IsNullOrWhiteSpace(_item.EnglishTitle) &&
                !string.Equals(_item.EnglishTitle.Trim(), primary?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return _item.EnglishTitle.Trim();
            }

            return null;
        }
    }

    public bool HasSecondaryTitle => !string.IsNullOrWhiteSpace(SecondaryTitle);

    public string? DisplaySynopsis => !string.IsNullOrEmpty(_item.RussianSynopsis) ? _item.RussianSynopsis : _item.Synopsis;

    public bool IsAnime => _item.MediaKind == MediaKind.Anime;
    public bool IsManga => _item.MediaKind != MediaKind.Anime;
}
