using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.Views.AnimeList;

public partial class AnimeReleaseMapView : Avalonia.Controls.UserControl
{
    public AnimeReleaseMapView()
    {
        InitializeComponent();
    }

    private void ScoreMenu_Opened(object? sender, EventArgs e)
    {
        if (sender is MenuFlyout flyout && flyout.Target is Control target && target.DataContext is AnimeItem item)
        {
            if (DataContext is AnimeListViewModel vm)
            {
                vm.OpenScoreMenuCommand.Execute(item);
            }
        }
    }

    private void ReleaseMapButton_Click(object? sender, RoutedEventArgs e) => ToggleReleaseMap();

    private void ReleaseCloseButton_Click(object? sender, RoutedEventArgs e) => HideReleaseMap();

    private void ReleaseMapOverlay_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F12 && e.Key != Key.Escape)
            return;

        HideReleaseMap();
        e.Handled = true;
    }

    public event EventHandler<bool>? IsMapVisibleChanged;
    public bool IsMapVisible { get; private set; }

    public void ToggleReleaseMap()
    {
        if (IsMapVisible)
        {
            HideReleaseMap();
            return;
        }

        ShowReleaseMap();
    }

    private void ShowReleaseMap()
    {
        BuildReleaseMap();
        IsMapVisible = true;
        this.IsVisible = true;
        ReleaseMapOverlay.IsVisible = true;
        ReleaseMapOverlay.Focus();
        IsMapVisibleChanged?.Invoke(this, true);
    }

    private void HideReleaseMap()
    {
        IsMapVisible = false;
        this.IsVisible = false;
        ReleaseMapOverlay.IsVisible = false;
        IsMapVisibleChanged?.Invoke(this, false);
    }

    private void BuildReleaseMap()
    {
        ReleaseTimelinePanel.Children.Clear();
        var palette = CreateReleasePalette();
        ApplyReleaseTheme(palette);

        if (DataContext is not AnimeListViewModel vm) return;
        var mapVm = new ReleaseMapViewModel(vm.AnimeItems);
        var releases = mapVm.GetUpcomingReleases().Take(24).ToList();
        ReleaseEmptyState.IsVisible = releases.Count == 0;

        if (releases.Count == 0)
        {
            ReleaseHeroAnimeTitle.Text = string.Empty;
            ReleaseHeroRussianTitle.Text = string.Empty;
            ReleaseHeroRussianTitle.IsVisible = false;
            ReleaseHeroKindText.Text = "Нет дат";
            ReleaseHeroCountdownText.Text = "после синхронизации";
            ReleaseHeroTimeText.Text = "--:--";
            ReleaseHeroWeekText.Text = "Нет будущих дат в текущих данных";
            CachedImage.SetSource(ReleaseHeroPoster, null);
            return;
        }

        var first = releases[0];
        ReleaseHeroAnimeTitle.Text = first.Title;
        ReleaseHeroRussianTitle.DataContext = first.Item;
        ReleaseHeroRussianTitle[!TextBlock.TextProperty] = new Avalonia.Data.Binding("RussianTitle");
        ReleaseHeroRussianTitle[!TextBlock.IsVisibleProperty] = new Avalonia.Data.Binding("RussianTitle") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty };
        ReleaseHeroKindText.Text = ReleaseMapViewModel.GetHeroReleaseKind(first);
        ReleaseHeroCountdownText.Text = ReleaseMapViewModel.FormatUntilRelease(first.ReleaseAt);
        ReleaseHeroTimeText.Text = first.ReleaseAt.ToString("HH:mm");
        ReleaseHeroWeekText.Text = ReleaseMapViewModel.FormatWeekReleaseSummary(releases);
        CachedImage.SetSource(ReleaseHeroPoster, first.PosterUrl);

        // Group releases by day and build timeline with headers
        DateTime? lastDate = null;
        int staggerIndex = 0;
        const int staggerMs = 40;

        foreach (var release in releases)
        {
            var releaseDate = release.ReleaseAt.Date;

            // Day group header
            if (lastDate == null || releaseDate != lastDate.Value)
            {
                var header = CreateDayHeader(releaseDate, palette);
                // Headers appear immediately
                ReleaseTimelinePanel.Children.Add(header);
                lastDate = releaseDate;
            }

            // Release card with cascade reveal
            var card = CreateReleaseCard(release, palette);
            card.Opacity = 0;
            card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translate(0,10px)");
            card.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition
                {
                    Property = Avalonia.Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(350),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                },
                new Avalonia.Animation.TransformOperationsTransition
                {
                    Property = Avalonia.Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(350),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                }
            };
            ReleaseTimelinePanel.Children.Add(card);

            // Schedule reveal with stagger
            var capturedCard = card;
            var delay = TimeSpan.FromMilliseconds(staggerIndex * staggerMs);
            DispatcherTimer.RunOnce(() =>
            {
                capturedCard.Opacity = 1;
                capturedCard.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translate(0,0)");
            }, delay);
            staggerIndex++;
        }

        // Same compensation as in settings: the decorated window viewport can be
        // taller than the visible area, so the last card needs scrollable air.
        ReleaseTimelinePanel.Children.Add(new Border
        {
            Height = 110,
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        });

        if (vm.ShikiMetadataService is { } shiki)
        {
            var tasks = releases.Select(r => shiki.EnsureLocalizedAsync(r.Item));
            _ = Task.WhenAll(tasks);
        }
    }


}
