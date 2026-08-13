using Kiriha.Core.Domain.Models.Entities;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.Views.AnimeList
{
    public partial class AnimeCardTemplates : ResourceDictionary
    {
        public AnimeCardTemplates()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private async void Poster_DoubleTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                if (sender is Control c && c.DataContext is Kiriha.Core.Domain.Models.Entities.AnimeEntity item)
                {
                    var topLevel = TopLevel.GetTopLevel(c);
                    // To get the AnimeListViewModel, we check if the TopLevel's content or the original control has it in data context.
                    // Actually, c.DataContext is AnimeEntity. We need the AnimeListViewModel which is on the parent UserControl.
                    // Wait! The DataContext of AnimeCardTemplates is not set. We should find the view model from the visual tree or use a service locator.
                    // Wait, the template has bindings like `Command="{Binding #animeListRoot.((vmAnimeList:AnimeListViewModel)DataContext).OpenScoreMenuCommand}"`.
                    // We can just find the AnimeListViewModel by walking the visual tree.
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
                Serilog.Log.Error(ex, "AnimeCardTemplates.Poster_DoubleTapped failed");
            }
        }

        private void ScoreMenu_Opened(object? sender, EventArgs e)
        {
            if (sender is MenuFlyout flyout && flyout.Target is Control target && target.DataContext is Kiriha.Core.Domain.Models.Entities.AnimeEntity item)
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
