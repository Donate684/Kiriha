using System;
using Kiriha.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kiriha.Infrastructure;
using Kiriha.ViewModels.AnimeList;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Views.AnimeList;

public partial class AnimeReleaseMapView : Avalonia.Controls.UserControl
{
    private Border CreateReleaseCard(ReleaseMapItem release, ReleasePalette palette)
    {
        var isPast = release.ReleaseAt < DateTime.UtcNow.AddMinutes(-10);
        var isSoon = !isPast && (release.ReleaseAt - DateTime.UtcNow).TotalHours <= 24;
        var accentColor = isSoon ? palette.WarmAccent : palette.CoolAccent;

        // Left accent bar
        var accentBar = new Border
        {
            Width = 4,
            CornerRadius = new Avalonia.CornerRadius(2, 0, 0, 2),
            Background = BrushFrom(accentColor),
        };
        Grid.SetColumn(accentBar, 0);

        var (cardContent, titleBlock) = CreateCardContent(release, accentColor, palette);

        var inner = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("4,*,Auto"),
            MinHeight = 68,
            Children = { accentBar, cardContent, CreateReleaseBadge(release, isSoon, palette) }
        };

        var card = new Border
        {
            CornerRadius = new Avalonia.CornerRadius(10),
            Background = BrushFrom(palette.CardBackground),
            BorderBrush = BrushFrom(palette.CardBorder),
            BorderThickness = new Avalonia.Thickness(1),
            ClipToBounds = true,
            Margin = new Avalonia.Thickness(0, 1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            Child = inner
        };

        card.PointerEntered += (_, _) =>
        {
            card.Background = BrushFrom(palette.CardHover);
            titleBlock.Foreground = BrushFrom(accentColor);
        };
        card.PointerExited += (_, _) =>
        {
            card.Background = BrushFrom(palette.CardBackground);
            titleBlock.Foreground = BrushFrom(palette.PrimaryText);
        };
        card.Tapped += async (_, e) =>
        {
            e.Handled = true;
            await OpenAnimeDetailsAsync(release.Item);
        };
        card.KeyDown += async (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                await OpenAnimeDetailsAsync(release.Item);
            }
        };

        return card;
    }

    private static (Control Content, TextBlock TitleBlock) CreateCardContent(ReleaseMapItem release, string accentColor, ReleasePalette palette)
    {
        // Poster
        var posterImage = new Image { Stretch = Stretch.UniformToFill };
        CachedImage.SetSource(posterImage, release.PosterUrl);
        var poster = new Border
        {
            Width = 46,
            Height = 62,
            CornerRadius = new Avalonia.CornerRadius(6),
            ClipToBounds = true,
            Background = BrushFrom(palette.PosterBackground),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(12, 10, 0, 10),
            Child = posterImage
        };

        // Date block (day + month + time)
        var dateStack = new StackPanel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Width = 52,
            Margin = new Avalonia.Thickness(12, 0, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = release.ReleaseAt.ToString("dd"),
                    FontSize = 24, FontWeight = FontWeight.Black,
                    Foreground = BrushFrom(accentColor),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = ReleaseMapViewModel.FormatMonthShort(release.ReleaseAt),
                    FontSize = 10, FontWeight = FontWeight.Bold,
                    Foreground = BrushFrom(palette.SecondaryText),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    LetterSpacing = 0.5
                },
                new Border
                {
                    Height = 1, Margin = new Avalonia.Thickness(4, 4),
                    Background = BrushFrom(palette.Divider)
                },
                new TextBlock
                {
                    Text = release.ReleaseAt.ToString("HH:mm"),
                    FontSize = 11, FontWeight = FontWeight.SemiBold,
                    Foreground = BrushFrom(accentColor),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Opacity = 0.85
                }
            }
        };

        // Info: title, russian title, meta pills
        var infoPanel = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(12, 10, 10, 10)
        };

        var titleBlock = new TextBlock
        {
            Text = release.Title,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = BrushFrom(palette.PrimaryText),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.BrushTransition
                {
                    Property = TextBlock.ForegroundProperty,
                    Duration = TimeSpan.FromMilliseconds(150)
                }
            }
        };
        infoPanel.Children.Add(titleBlock);

        infoPanel.Children.Add(new TextBlock
        {
            DataContext = release.Item,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(AnimeEntity.SecondaryTitle)),
            FontSize = 11,
            Foreground = BrushFrom(palette.SecondaryTitleText),
            TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(AnimeEntity.HasSecondaryTitle))
        });

        // Meta pills
        var metaRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };
        metaRow.Children.Add(MakePill(release.Kind, palette));
        metaRow.Children.Add(MakePill(ReleaseMapViewModel.FormatUntilRelease(release.ReleaseAt), palette));
        infoPanel.Children.Add(metaRow);

        // Assemble row: [poster][date][info]
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*")
        };
        Grid.SetColumn(poster, 0);
        Grid.SetColumn(dateStack, 1);
        Grid.SetColumn(infoPanel, 2);
        row.Children.Add(poster);
        row.Children.Add(dateStack);
        row.Children.Add(infoPanel);

        Grid.SetColumn(row, 1);
        return (row, titleBlock);
    }

    private static Border MakePill(string text, ReleasePalette palette)
    {
        return new Border
        {
            Padding = new Avalonia.Thickness(7, 2),
            CornerRadius = new Avalonia.CornerRadius(4),
            Background = BrushFrom(palette.PillBg),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeight.Medium,
                Foreground = BrushFrom(palette.SecondaryText)
            }
        };
    }

    private static Control CreateReleaseBadge(ReleaseMapItem release, bool isSoon, ReleasePalette palette)
    {
        var isPast = release.ReleaseAt < DateTime.UtcNow.AddMinutes(-10);
        var bg = isPast ? palette.PillBg : isSoon ? palette.WarmBadge : palette.CoolBadge;
        var fg = isPast ? palette.CoolAccent : isSoon ? palette.WarmBadgeText : palette.CoolBadgeText;

        var content = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Spacing = 2
        };

        var iconKind = isPast
            ? Material.Icons.MaterialIconKind.CheckCircleOutline
            : (isSoon ? Material.Icons.MaterialIconKind.Fire : Material.Icons.MaterialIconKind.ClockOutline);

        content.Children.Add(new Material.Icons.Avalonia.MaterialIcon
        {
            Kind = iconKind,
            Width = 14,
            Height = 14,
            Foreground = BrushFrom(fg),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = ReleaseMapViewModel.FormatBadgeDate(release.ReleaseAt),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = BrushFrom(fg),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });

        var badge = new Border
        {
            Width = 64,
            Padding = new Avalonia.Thickness(6, 8),
            Background = BrushFrom(bg),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Child = content
        };
        Grid.SetColumn(badge, 2);
        return badge;
    }

    private static Control CreateDayHeader(string label, ReleasePalette palette)
    {

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Avalonia.Thickness(0, 12, 0, 4) };
        var text = new TextBlock
        {
            Text = label.ToUpper(ReleaseMapViewModel.GetReleaseCulture()),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.2,
            Foreground = BrushFrom(palette.DayHeaderText),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(2, 0, 12, 0)
        };
        var line = new Border
        {
            Height = 1,
            Background = BrushFrom(palette.Divider),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Opacity = 0.3
        };
        Grid.SetColumn(line, 1);
        grid.Children.Add(text);
        grid.Children.Add(line);
        return grid;
    }

}
