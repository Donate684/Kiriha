using System.ComponentModel;
using System.Globalization;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Core.Domain.Models.Entities;

public delegate string GetLocDelegate(string key, params object?[] args);

public partial class AnimeEntityPresentation : INotifyPropertyChanged
{
    private sealed class DefaultFallbackLocalizer : ILocalizer
    {
        public string GetLoc(string key) => key;
        public string GetLoc(string key, params object?[] args) => args != null && args.Length > 0 ? string.Format(key, args) : key;
        public CultureInfo CurrentCulture => CultureInfo.CurrentCulture;
    }

    private sealed class DelegateLocalizer(GetLocDelegate del) : ILocalizer
    {
        public string GetLoc(string key) => del(key);
        public string GetLoc(string key, params object?[] args) => del(key, args ?? []);
        public CultureInfo CurrentCulture => CultureInfo.CurrentCulture;
    }

    private static ILocalizer _defaultLocalizer = new DefaultFallbackLocalizer();
    public static ILocalizer DefaultLocalizer
    {
        get => _defaultLocalizer;
        set => _defaultLocalizer = value ?? new DefaultFallbackLocalizer();
    }

    public static TimeProvider DefaultClock { get; set; } = TimeProvider.System;
    public static Func<bool> DefaultGetUseRussianTitles { get; set; } = () => false;
    public static Func<bool> DefaultGetPreferRussianDisplayTitle { get; set; } = () => true;
    public static Func<bool> DefaultGetUseRussianDescriptions { get; set; } = () => true;

    public static void SetDefaultGetLoc(GetLocDelegate getLoc)
    {
        DefaultLocalizer = getLoc != null ? new DelegateLocalizer(getLoc) : new DefaultFallbackLocalizer();
    }

    public static Func<bool> GetUseRussianTitles
    {
        get => DefaultGetUseRussianTitles;
        set => DefaultGetUseRussianTitles = value ?? (() => false);
    }

    public static Func<bool> GetPreferRussianDisplayTitle
    {
        get => DefaultGetPreferRussianDisplayTitle;
        set => DefaultGetPreferRussianDisplayTitle = value ?? (() => true);
    }

    public static Func<bool> GetUseRussianDescriptions
    {
        get => DefaultGetUseRussianDescriptions;
        set => DefaultGetUseRussianDescriptions = value ?? (() => true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AnimeEntity _item;
    private readonly ILocalizer? _localizer;
    private readonly TimeProvider? _clock;
    private readonly Func<bool>? _getUseRussianTitles;
    private readonly Func<bool>? _getPreferRussianDisplayTitle;
    private readonly Func<bool>? _getUseRussianDescriptions;
    private DateTime _now;

    public AnimeEntityPresentation(AnimeEntity item)
        : this(item, null, null, null, null, null)
    {
    }

    public AnimeEntityPresentation(AnimeEntity item, DateTime now)
        : this(item, null, null, null, null, null)
    {
        _now = now;
    }

    public AnimeEntityPresentation(
        AnimeEntity item,
        ILocalizer? localizer = null,
        TimeProvider? clock = null,
        Func<bool>? getUseRussianTitles = null)
        : this(item, localizer, clock, getUseRussianTitles, null, null)
    {
    }

    public AnimeEntityPresentation(
        AnimeEntity item,
        ILocalizer? localizer,
        TimeProvider? clock,
        Func<bool>? getUseRussianTitles,
        Func<bool>? getPreferRussianDisplayTitle,
        Func<bool>? getUseRussianDescriptions)
    {
        _item = item;
        _localizer = localizer;
        _clock = clock;
        _getUseRussianTitles = getUseRussianTitles;
        _getPreferRussianDisplayTitle = getPreferRussianDisplayTitle;
        _getUseRussianDescriptions = getUseRussianDescriptions;
        _now = EffectiveClock.GetUtcNow().UtcDateTime;
    }

    public ILocalizer EffectiveLocalizer => _localizer ?? DefaultLocalizer;
    public TimeProvider EffectiveClock => _clock ?? DefaultClock;
    public bool EffectiveUseRussianTitles => (_getUseRussianTitles ?? DefaultGetUseRussianTitles)();
    public bool EffectivePreferRussianDisplayTitle => (_getPreferRussianDisplayTitle ?? DefaultGetPreferRussianDisplayTitle)();
    public bool EffectiveUseRussianDescriptions => (_getUseRussianDescriptions ?? DefaultGetUseRussianDescriptions)();
    public CultureInfo EffectiveCulture => EffectiveLocalizer.CurrentCulture;

    private string GetLoc(string key, params object?[] args) => EffectiveLocalizer.GetLoc(key, args);

    private string? _cachedSecondaryTitle;
    private bool _secondaryTitleComputed;
    private bool _cachedUseRussianTitles;

    private string? _cachedJapaneseTitle;
    private bool _japaneseTitleComputed;
    private bool _cachedPreferRussianDisplayTitle;

    public void RaiseAll()
    {
        _now = EffectiveClock.GetUtcNow().UtcDateTime;
        _secondaryTitleComputed = false;
        _japaneseTitleComputed = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string DisplayTitle => EffectivePreferRussianDisplayTitle
        ? (!string.IsNullOrEmpty(_item.RussianTitle) ? _item.RussianTitle : _item.Title)
        : (!string.IsNullOrEmpty(_item.Title) ? _item.Title : (_item.RussianTitle ?? string.Empty));

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

    public string? JapaneseTitle
    {
        get
        {
            bool preferRussian = EffectivePreferRussianDisplayTitle;
            if (_japaneseTitleComputed && _cachedPreferRussianDisplayTitle == preferRussian)
            {
                return _cachedJapaneseTitle;
            }

            _cachedJapaneseTitle = ComputeJapaneseTitle();
            _cachedPreferRussianDisplayTitle = preferRussian;
            _japaneseTitleComputed = true;
            return _cachedJapaneseTitle;
        }
    }

    private string? ComputeJapaneseTitle()
    {
        var top = DisplayTitle;

        if (!string.IsNullOrWhiteSpace(_item.JapaneseTitle))
        {
            var ja = _item.JapaneseTitle.Trim();
            if (!string.Equals(ja, top, StringComparison.OrdinalIgnoreCase))
            {
                return ja;
            }
        }

        if (!string.IsNullOrWhiteSpace(_item.Title))
        {
            var title = _item.Title.Trim();
            if (!string.Equals(title, top, StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }
        }

        if (!string.IsNullOrWhiteSpace(_item.EnglishTitle))
        {
            var en = _item.EnglishTitle.Trim();
            if (!string.Equals(en, top, StringComparison.OrdinalIgnoreCase))
            {
                return en;
            }
        }

        return null;
    }

    public bool HasJapaneseTitle => !string.IsNullOrWhiteSpace(JapaneseTitle);

    public string? DisplaySynopsis => EffectiveUseRussianDescriptions
        ? (!string.IsNullOrEmpty(_item.RussianSynopsis) ? _item.RussianSynopsis : _item.Synopsis)
        : (!string.IsNullOrEmpty(_item.Synopsis) ? _item.Synopsis : _item.RussianSynopsis);

    public bool IsAnime => _item.MediaKind == MediaKind.Anime;
    public bool IsManga => _item.MediaKind != MediaKind.Anime;
}
