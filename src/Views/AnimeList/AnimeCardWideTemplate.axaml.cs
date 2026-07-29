using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.Views.AnimeList
{
    public partial class AnimeCardWideTemplate : ResourceDictionary
    {
        public AnimeCardWideTemplate()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private async void Poster_DoubleTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                if (sender is Control c && c.DataContext is Models.AnimeItem item)
                {
                    var topLevel = TopLevel.GetTopLevel(c);
                    var parent = c.Parent;
                    AnimeListViewModel? vm = null;
                    while (parent != null)
                    {
                        if (parent.DataContext is AnimeListViewModel parentVm)
                        {
                            vm = parentVm;
                            break;
                        }
                        parent = parent.Parent;
                    }

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

        private void ScoreMenu_Opened(object? sender, EventArgs e)
        {
            if (sender is MenuFlyout flyout && flyout.Target is Control target && target.DataContext is Models.AnimeItem item)
            {
                var parent = target.Parent;
                AnimeListViewModel? vm = null;
                while (parent != null)
                {
                    if (parent.DataContext is AnimeListViewModel parentVm)
                    {
                        vm = parentVm;
                        break;
                    }
                    parent = parent.Parent;
                }

                if (vm != null)
                {
                    vm.OpenScoreMenuCommand.Execute(item);
                }
            }
        }
    }
}
