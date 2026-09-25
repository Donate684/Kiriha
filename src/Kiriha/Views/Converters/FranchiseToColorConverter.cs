using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Views.Converters;

public class FranchiseToColorConverter : IValueConverter
{
    private static readonly ISolidColorBrush SequelBrush = new SolidColorBrush(Color.Parse("#6A1B9A")); // Purple
    private static readonly ISolidColorBrush DroppedBrush = new SolidColorBrush(Color.Parse("#B71C1C")); // Deep Carmine Red
    private static readonly ISolidColorBrush WatchingBrush = new SolidColorBrush(Color.Parse("#2E7D32")); // Green
    private static readonly ISolidColorBrush SpinOffBrush = new SolidColorBrush(Color.Parse("#00838F")); // Teal
    private static readonly ISolidColorBrush PlanBrush = new SolidColorBrush(Color.Parse("#455A64")); // Slate Grey
    private static readonly ISolidColorBrush DefaultBrush = new SolidColorBrush(Color.Parse("#546E7A"));
    private static readonly ISolidColorBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FranchiseContext ctx && ctx.HasRelation)
        {
            if (ctx.UserStatus == UserAnimeStatus.Dropped)
                return DroppedBrush;

            return ctx.Relation switch
            {
                FranchiseRelationKind.Sequel => SequelBrush,
                FranchiseRelationKind.SpinOff or FranchiseRelationKind.SideStory => SpinOffBrush,
                _ => ctx.UserStatus switch
                {
                    UserAnimeStatus.Completed => SequelBrush,
                    UserAnimeStatus.Watching => WatchingBrush,
                    UserAnimeStatus.PlanToWatch => PlanBrush,
                    _ => DefaultBrush
                }
            };
        }

        return TransparentBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
