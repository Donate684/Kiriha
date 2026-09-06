using System;
using Kiriha.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Kiriha.Infrastructure;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Avalonia.Markup.Xaml;
using Kiriha.Localization;
using Kiriha.Core.Domain.Models.Entities;
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
        if (sender is MenuFlyout flyout && flyout.Target is Control target && target.DataContext is AnimeEntity item)
        {
            if (DataContext is AnimeListViewModel vm)
            {
                vm.OpenScoreMenuCommand.Execute(item);
            }
        }
    }

    private void ReleaseMapButton_Click(object? sender, RoutedEventArgs e) => ToggleReleaseMap();

    private void ReleaseCloseButton_Click(object? sender, RoutedEventArgs e) => HideReleaseMap();

    private AnimeEntity? _currentHeroItem;
    private bool _isOpeningDetails;

    private async void ReleaseHero_Tapped(object? sender, TappedEventArgs e)
    {
        if (_currentHeroItem != null)
        {
            e.Handled = true;
            await OpenAnimeDetailsAsync(_currentHeroItem);
        }
    }

    private async void ReleaseHero_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && _currentHeroItem != null)
        {
            e.Handled = true;
            await OpenAnimeDetailsAsync(_currentHeroItem);
        }
    }

    public async Task OpenAnimeDetailsAsync(AnimeEntity item)
    {
        if (_isOpeningDetails) return;
        _isOpeningDetails = true;
        try
        {
            if (DataContext is not AnimeListViewModel vm) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            if (await vm.DialogService.ShowAnimeDetailsAsync(topLevel, item))
            {
                vm.RefreshAfterDetailsEdit();
                BuildReleaseMap();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "AnimeReleaseMapView.OpenAnimeDetailsAsync failed");
        }
        finally
        {
            _isOpeningDetails = false;
        }
    }

    private void ReleaseMapOverlay_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F12 && e.Key != Key.Escape)
            return;

        HideReleaseMap();
        e.Handled = true;
    }

    public event EventHandler<bool>? IsMapVisibleChanged;
    public bool IsMapVisible { get; private set; }
    private ReleaseMapFilter _currentFilter = ReleaseMapFilter.Upcoming;

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
        UpdateFilterButtonsState();
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

    private void FilterUpcoming_Click(object? sender, RoutedEventArgs e) => SetFilter(ReleaseMapFilter.Upcoming);
    private void FilterPast_Click(object? sender, RoutedEventArgs e) => SetFilter(ReleaseMapFilter.Past);

    private void SetFilter(ReleaseMapFilter filter)
    {
        if (_currentFilter == filter) return;
        _currentFilter = filter;
        UpdateFilterButtonsState();
        BuildReleaseMap();
    }

    private void UpdateFilterButtonsState()
    {
        SetButtonActive(FilterUpcomingBtn, _currentFilter == ReleaseMapFilter.Upcoming);
        SetButtonActive(FilterPastBtn, _currentFilter == ReleaseMapFilter.Past);
    }

    private static void SetButtonActive(Button? btn, bool active)
    {
        if (btn == null) return;
        if (active)
        {
            if (!btn.Classes.Contains("Active"))
                btn.Classes.Add("Active");
        }
        else
        {
            btn.Classes.Remove("Active");
        }
    }

    private void BuildReleaseMap()
    {
        ReleaseTimelinePanel.Children.Clear();
        var palette = CreateReleasePalette();
        ApplyReleaseTheme(palette);

        if (DataContext is not AnimeListViewModel vm) return;
        var mapVm = new ReleaseMapViewModel(vm.AnimeItems);
        var groups = mapVm.GetReleaseGroups(_currentFilter, 24).ToList();
        var releases = groups.SelectMany(g => g.Releases).ToList();
        ReleaseEmptyState.IsVisible = releases.Count == 0;

        if (releases.Count == 0)
        {
            _currentHeroItem = null;
            ReleaseHeroClickableArea.Cursor = Cursor.Default;
            ReleaseHeroAnimeTitle.Text = string.Empty;
            ReleaseHeroRussianTitle.Text = string.Empty;
            ReleaseHeroRussianTitle.IsVisible = false;
            ReleaseHeroKindText.Text = ReleaseMapViewModel.GetNoDatesText();
            ReleaseHeroCountdownText.Text = ReleaseMapViewModel.GetAfterSyncText();
            ReleaseHeroTimeText.Text = "--:--";
            ReleaseHeroWeekText.Text = ReleaseMapViewModel.GetNoReleasesHeroText(_currentFilter);
            CachedImage.SetSource(ReleaseHeroPoster, null);

            ReleaseEmptyTitle.Text = ReleaseMapViewModel.GetNoReleasesTitle(_currentFilter);
            ReleaseEmptySubtitle.Text = ReleaseMapViewModel.GetNoReleasesSubtitle(_currentFilter);

            return;
        }

        var first = releases[0];
        _currentHeroItem = first.Item;
        ReleaseHeroClickableArea.Cursor = new Cursor(StandardCursorType.Hand);
        ReleaseHeroAnimeTitle.Text = first.Title;
        ReleaseHeroRussianTitle.DataContext = first.Item;
        ReleaseHeroRussianTitle[!TextBlock.TextProperty] = new Avalonia.Data.Binding("RussianTitle");
        ReleaseHeroRussianTitle[!TextBlock.IsVisibleProperty] = new Avalonia.Data.Binding("RussianTitle") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty };

        if (_currentFilter == ReleaseMapFilter.Past)
        {
            ReleaseHeroKindText.Text = ReleaseMapViewModel.GetLatestEpText();
            ReleaseHeroCountdownText.Text = ReleaseMapViewModel.FormatUntilRelease(first.ReleaseAt);
            ReleaseHeroTimeText.Text = first.ReleaseAt.ToString("HH:mm");
            ReleaseHeroWeekText.Text = ReleaseMapViewModel.FormatPastReleaseSummary(releases);
        }
        else
        {
            ReleaseHeroKindText.Text = ReleaseMapViewModel.GetHeroReleaseKind(first);
            ReleaseHeroCountdownText.Text = ReleaseMapViewModel.FormatUntilRelease(first.ReleaseAt);
            ReleaseHeroTimeText.Text = first.ReleaseAt.ToString("HH:mm");
            ReleaseHeroWeekText.Text = ReleaseMapViewModel.FormatWeekReleaseSummary(releases);
        }

        CachedImage.SetSource(ReleaseHeroPoster, first.PosterUrl);

        int staggerIndex = 0;
        const int staggerMs = 40;

        foreach (var group in groups)
        {
            var header = CreateDayHeader(group.Label, palette);
            ReleaseTimelinePanel.Children.Add(header);

            foreach (var release in group.Releases)
            {
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
                    },
                    new Avalonia.Animation.BrushTransition
                    {
                        Property = Border.BackgroundProperty,
                        Duration = TimeSpan.FromMilliseconds(150)
                    }
                };
                ReleaseTimelinePanel.Children.Add(card);

                var capturedCard = card;
                var delay = TimeSpan.FromMilliseconds(staggerIndex * staggerMs);
                DispatcherTimer.RunOnce(() =>
                {
                    capturedCard.Opacity = 1;
                    capturedCard.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translate(0,0)");
                }, delay);
                staggerIndex++;
            }
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
