using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Avalonia.Styling;

namespace Kiriha.Views.AnimeList;

public partial class AnimeReleaseMapView : Avalonia.Controls.UserControl
{
    private ReleasePalette CreateReleasePalette()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        return dark
            ? new ReleasePalette(
                Surface: "#FF0D1117",
                HeaderSurface: "#FF1A1F2E",
                HeaderScrimStart: "#F21A1F2E",
                HeaderScrimMid: "#D01A1F2E",
                HeaderScrimEnd: "#101A1F2E",
                CardBackground: "#161B27",
                CardBorder: "#22FFFFFF",
                CardHover: "#1E2435",
                PosterBackground: "#0F1420",
                PrimaryText: "#F0FFFFFF",
                SecondaryTitleText: "#FF8BD3FF",
                SecondaryText: "#80FFFFFF",
                CoolAccent: "#FF8BD3FF",
                WarmAccent: "#FFE9C46A",
                CoolBadge: "#FF1E3A5F",
                WarmBadge: "#FF3D2A0A",
                CoolBadgeText: "#FF8BD3FF",
                WarmBadgeText: "#FFE9C46A",
                PillBg: "#18FFFFFF",
                DayHeaderText: "#55FFFFFF",
                Divider: "#20FFFFFF")
            : new ReleasePalette(
                Surface: "#EEFFFFFF",
                HeaderSurface: "#FFEAF3FB",
                HeaderScrimStart: "#F8EAF3FB",
                HeaderScrimMid: "#DDEAF3FB",
                HeaderScrimEnd: "#40EAF3FB",
                CardBackground: "#F7FFFFFF",
                CardBorder: "#18000000",
                CardHover: "#FFFFFFFF",
                PosterBackground: "#10000000",
                PrimaryText: "#E4000000",
                SecondaryTitleText: "#FF087CC5",
                SecondaryText: "#99000000",
                CoolAccent: "#FF087CC5",
                WarmAccent: "#FFD39A18",
                CoolBadge: "#FFE6F3FC",
                WarmBadge: "#FFFFF3D0",
                CoolBadgeText: "#FF087CC5",
                WarmBadgeText: "#FF8A6200",
                PillBg: "#0E000000",
                DayHeaderText: "#66000000",
                Divider: "#16000000");
    }

    private void ApplyReleaseTheme(ReleasePalette palette)
    {
        ReleaseMapOverlay.Background = BrushFrom(palette.Surface);
        ReleaseMapOverlay.BorderBrush = BrushFrom(palette.CardBorder);
    }



    private static string ToTransparent(string color)
    {
        if (color.Length == 9 && color[0] == '#')
            return "#00" + color[3..];

        if (color.Length == 7 && color[0] == '#')
            return "#00" + color[1..];

        return "#00000000";
    }

    private static IBrush BrushFrom(string color) => new SolidColorBrush(Color.Parse(color));
}
