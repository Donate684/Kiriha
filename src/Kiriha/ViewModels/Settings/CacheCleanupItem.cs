using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Services.Data.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Settings;

public partial class CacheCleanupItem : ObservableObject
{
    public CacheCleanupTarget Target { get; }
    public string TitleKey { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStats))]
    private int _itemCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStats))]
    private long _sizeBytes;

    private readonly Kiriha.Core.Abstractions.Services.ILocalizer _localizer;

    public string DisplayStats => SizeBytes > 0
        ? _localizer.GetLoc("settings.cache.stats_with_size", ItemCount, FormatBytes(SizeBytes))
        : _localizer.GetLoc("settings.cache.stats_items", ItemCount);

    public CacheCleanupItem(CacheCleanupTarget target, string titleKey, Kiriha.Core.Abstractions.Services.ILocalizer localizer)
    {
        Target = target;
        TitleKey = titleKey;
        _localizer = localizer;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
