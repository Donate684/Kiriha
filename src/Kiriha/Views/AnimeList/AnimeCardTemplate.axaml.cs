using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.Views.AnimeList
{
    public partial class AnimeCardTemplate : ResourceDictionary
    {
        public AnimeCardTemplate()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private static AnimeListViewModel? FindAnimeListViewModel(Control control)
        {
            var parent = control.Parent;
            while (parent != null)
            {
                if (parent.DataContext is AnimeListViewModel parentVm)
                    return parentVm;
                parent = parent.Parent;
            }
            return null;
        }

        private async void Poster_DoubleTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                if (sender is Control c && c.DataContext is AnimeEntity item)
                {
                    var topLevel = TopLevel.GetTopLevel(c);
                    var vm = FindAnimeListViewModel(c);

                    if (vm != null && topLevel != null)
                    {
                        if (await vm.DialogService.ShowAnimeDetailsAsync(topLevel, item))
                        {
                            vm.RefreshAfterDetailsEdit();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "AnimeCardTemplate.Poster_DoubleTapped failed");
            }
        }

        private void ScoreButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not AnimeEntity item)
                return;

            var vm = FindAnimeListViewModel(button);
            if (vm is null)
                return;

            var flyout = FlyoutBase.GetAttachedFlyout(button) as MenuFlyout;
            if (flyout is null)
                return;

            var scores = vm.AvailableScores;
            if (flyout.ItemsSource is null)
            {
                flyout.ItemsSource = scores;
            }
            else if (!ReferenceEquals(flyout.ItemsSource, scores))
            {
                // MenuFlyout caches its MenuFlyoutPresenter after the first open, so
                // replace the flyout if AvailableScores was rebuilt (e.g. locale change).
                flyout = new MenuFlyout { ItemsSource = scores };
                flyout.FlyoutPresenterClasses.Add("ScoreFlyout");
                FlyoutBase.SetAttachedFlyout(button, flyout);
            }

            vm.OpenScoreMenuCommand.Execute(item);
            FlyoutBase.ShowAttachedFlyout(button);
        }
    }
}
