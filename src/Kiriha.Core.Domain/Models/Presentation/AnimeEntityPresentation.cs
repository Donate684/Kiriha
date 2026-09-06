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

    private string? _cachedSecondaryTitle;
    private bool _secondaryTitleComputed;
    private bool _cachedUseRussianTitles;

    public void RaiseAll()
    {
        _now = DateTime.UtcNow;
        _secondaryTitleComputed = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string DisplayTitle => !string.IsNullOrEmpty(_item.RussianTitle) ? _item.RussianTitle : _item.Title;

    public string? SecondaryTitle
    {
        get
        {
            bool useRussian = GetUseRussianTitles();
            if (_secondaryTitleComputed && _cachedUseRussianTitles == useRussian)
            {
                return _cachedSecondaryTitle;
            }

            _cachedSecondaryTitle = ComputeSecondaryTitle(useRussian);
            _cachedUseRussianTitles = useRussian;
            _secondaryTitleComputed = true;
            return _cachedSecondaryTitle;
        }
    }

    private string? ComputeSecondaryTitle(bool useRussian)
    {
        var primary = !string.IsNullOrWhiteSpace(_item.Title)
            ? _item.Title
            : (!string.IsNullOrWhiteSpace(_item.EnglishTitle) ? _item.EnglishTitle : _item.RussianTitle);

        ReadOnlySpan<char> primaryTrimmed = primary != null ? primary.AsSpan().Trim() : ReadOnlySpan<char>.Empty;

        if (useRussian)
        {
            if (!string.IsNullOrWhiteSpace(_item.RussianTitle))
            {
                var ruSpan = _item.RussianTitle.AsSpan().Trim();
                if (!ruSpan.Equals(primaryTrimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return _item.RussianTitle.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(_item.EnglishTitle))
            {
                var enSpan = _item.EnglishTitle.AsSpan().Trim();
                if (!enSpan.Equals(primaryTrimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return _item.EnglishTitle.Trim();
                }
            }

            return null;
        }

        if (!string.IsNullOrWhiteSpace(_item.EnglishTitle))
        {
            var enSpan = _item.EnglishTitle.AsSpan().Trim();
            if (!enSpan.Equals(primaryTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                return _item.EnglishTitle.Trim();
            }
        }

        return null;
    }

    public bool HasSecondaryTitle => !string.IsNullOrWhiteSpace(SecondaryTitle);

    public string? DisplaySynopsis => !string.IsNullOrEmpty(_item.RussianSynopsis) ? _item.RussianSynopsis : _item.Synopsis;

    public bool IsAnime => _item.MediaKind == MediaKind.Anime;
    public bool IsManga => _item.MediaKind != MediaKind.Anime;
}
