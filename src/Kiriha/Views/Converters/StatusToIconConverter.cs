using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Core.Domain.Models.Entities;
using Material.Icons;

namespace Kiriha.Views.Converters;

public class StatusToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is UserAnimeStatus status)
        {
            return status switch
            {
                UserAnimeStatus.Watching => MaterialIconKind.PlayCircleOutline,
                UserAnimeStatus.PlanToWatch => MaterialIconKind.BookmarkOutline,
                UserAnimeStatus.OnHold => MaterialIconKind.PauseCircleOutline,
                UserAnimeStatus.Completed => MaterialIconKind.CheckCircleOutline,
                UserAnimeStatus.Dropped => MaterialIconKind.CloseCircleOutline,
                _ => MaterialIconKind.HelpCircleOutline
            };
        }
        return MaterialIconKind.HelpCircleOutline;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
