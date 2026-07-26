using System;
using Kiriha.Models;
namespace Kiriha.Views.AnimeList;

public partial class AnimeReleaseMapView : Avalonia.Controls.UserControl
{

    private sealed record ReleasePalette(
        string Surface,
        string HeaderSurface,
        string HeaderScrimStart,
        string HeaderScrimMid,
        string HeaderScrimEnd,
        string CardBackground,
        string CardBorder,
        string CardHover,
        string PosterBackground,
        string PrimaryText,
        string SecondaryTitleText,
        string SecondaryText,
        string CoolAccent,
        string WarmAccent,
        string CoolBadge,
        string WarmBadge,
        string CoolBadgeText,
        string WarmBadgeText,
        string PillBg,
        string DayHeaderText,
        string Divider);
}
