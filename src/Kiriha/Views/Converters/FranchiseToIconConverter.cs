using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Core.Domain.Models.Entities;
using Material.Icons;

namespace Kiriha.Views.Converters;

public class FranchiseToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FranchiseContext ctx && ctx.HasRelation)
        {
            if (ctx.UserStatus == UserAnimeStatus.Dropped)
                return MaterialIconKind.CloseCircleOutline;

            return ctx.Relation switch
            {
                FranchiseRelationKind.Sequel => MaterialIconKind.SkipNext,
                FranchiseRelationKind.SpinOff or FranchiseRelationKind.SideStory => MaterialIconKind.CompassOutline,
                _ => ctx.UserStatus switch
                {
                    UserAnimeStatus.Completed => MaterialIconKind.CheckCircleOutline,
                    UserAnimeStatus.Watching => MaterialIconKind.PlayCircleOutline,
                    UserAnimeStatus.PlanToWatch => MaterialIconKind.BookmarkOutline,
                    _ => MaterialIconKind.LinkVariant
                }
            };
        }

        return MaterialIconKind.LinkVariant;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
