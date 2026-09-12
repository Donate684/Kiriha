using System;
using System.ComponentModel;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Models.Entities;

public delegate string GetLocDelegate(string key, params object?[] args);

public partial class AnimeEntityPresentation : INotifyPropertyChanged
{
    private sealed class DefaultFallbackLocalizer : ILocalizer
    {
        public string GetLoc(string key) => key;
        public string GetLoc(string key, params object?[] args) => args != null && args.Length > 0 ? string.Format(key, args) : key;
    }

    private sealed class DelegateLocalizer(GetLocDelegate del) : ILocalizer
    {
        public string GetLoc(string key) => del(key);
        public string GetLoc(string key, params object?[] args) => del(key, args ?? []);
    }

    private static ILocalizer _defaultLocalizer = new DefaultFallbackLocalizer();
    public static ILocalizer DefaultLocalizer
    {
        get => _defaultLocalizer;
        set => _defaultLocalizer = value ?? new DefaultFallbackLocalizer();
    }

    public static TimeProvider DefaultClock { get; set; } = TimeProvider.System;
    public static Func<bool> DefaultGetUseRussianTitles { get; set; } = () => false;

    public static void SetDefaultGetLoc(GetLocDelegate getLoc)
    {
        DefaultLocalizer = getLoc != null ? new DelegateLocalizer(getLoc) : new DefaultFallbackLocalizer();
    }

    public static Func<bool> GetUseRussianTitles
    {
        get => DefaultGetUseRussianTitles;
        set => DefaultGetUseRussianTitles = value ?? (() => false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AnimeEntity _item;
    private readonly ILocalizer? _localizer;
    private readonly TimeProvider? _clock;
    private readonly Func<bool>? _getUseRussianTitles;
    private DateTime _now;

    public AnimeEntityPresentation(AnimeEntity item)
        : this(item, null, null, null)
    {
    }

    public AnimeEntityPresentation(AnimeEntity item, DateTime now)
        : this(item, null, null, null)
    {
        _now = now;
    }

    public AnimeEntityPresentation(
        AnimeEntity item,
        ILocalizer? localizer = null,
        TimeProvider? clock = null,
        Func<bool>? getUseRussianTitles = null)
    {
        _item = item;
        _localizer = localizer;
        _clock = clock;
        _getUseRussianTitles = getUseRussianTitles;
        _now = EffectiveClock.GetUtcNow().UtcDateTime;
    }

    public ILocalizer EffectiveLocalizer => _localizer ?? DefaultLocalizer;
    public TimeProvider EffectiveClock => _clock ?? DefaultClock;
    public bool EffectiveUseRussianTitles => (_getUseRussianTitles ?? DefaultGetUseRussianTitles)();

    private string GetLoc(string key, params object?[] args) => EffectiveLocalizer.GetLoc(key, args);

    private string? _cachedSecondaryTitle;
    private bool _secondaryTitleComputed;
    private bool _cachedUseRussianTitles;

    public void RaiseAll()
    {
        _now = EffectiveClock.GetUtcNow().UtcDateTime;
        _secondaryTitleComputed = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string DisplayTitle => !string.IsNullOrEmpty(_item.RussianTitle) ? _item.RussianTitle : _item.Title;

    public string? SecondaryTitle
    {
        get
        {
            bool useRussian = EffectiveUseRussianTitles;
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
