using Kiriha.Core.Abstractions.Models.Entities;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.ViewModels.Seasonal;

namespace Kiriha.Views.Seasonal;

public partial class SeasonalCardTemplate : ResourceDictionary
{
    private readonly SeasonalHideConfirmController _hideConfirmController = new();

    public SeasonalCardTemplate()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private SeasonalViewModel? GetViewModel(Control c)
    {
        var parent = c.Parent;
        while (parent != null)
        {
            if (parent.DataContext is SeasonalViewModel vm)
                return vm;
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
                var vm = GetViewModel(c);
                if (vm != null)
                {
                    await vm.DialogService.ShowAnimeDetailsAsync(TopLevel.GetTopLevel(c) ?? c, item);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalCardTemplate.Poster_DoubleTapped failed");
        }
    }

    private void HideBtn_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c)
        {
            var vm = GetViewModel(c);
            if (vm != null)
            {
                _hideConfirmController.HandleHideBtnTapped(sender, e, vm);
            }
        }
    }

    private void QuickAddBtn_Tapped(object? sender, TappedEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (sender is Control c && c.ContextFlyout != null)
            {
                c.ContextFlyout.ShowAt(c);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalCardTemplate.QuickAddBtn_Tapped failed");
        }
    }

    private void QuickAddMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuItem menuItem &&
                menuItem.Tag is string statusStr && Enum.TryParse<UserAnimeStatus>(statusStr, out var status) &&
                menuItem.DataContext is AnimeEntity item)
            {
                var vm = GetViewModel(menuItem);
                if (vm != null)
                {
                    _ = vm.QuickAddToList(item, status);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalCardTemplate.QuickAddMenuItem_Click failed");
        }
    }
}
