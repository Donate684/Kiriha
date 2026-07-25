using System;
using Avalonia.Controls;
using Kiriha.Models;
using Kiriha.ViewModels.Seasonal;

namespace Kiriha.Views.Seasonal;

public class SeasonalHideConfirmController
{
    private AnimeItem? _hideConfirmItem;
    private Avalonia.Threading.DispatcherTimer? _hideConfirmTimer;
    private static readonly TimeSpan HideConfirmTimeout = TimeSpan.FromSeconds(3);

    public void HandleHideBtnTapped(object? sender, Avalonia.Input.TappedEventArgs e, SeasonalViewModel vm)
    {
        try
        {
            if (sender is not Control c || c.DataContext is not AnimeItem item) return;
            e.Handled = true;

            if (item.IsHideConfirming)
            {
                ResetHideConfirm();
                vm.ToggleHiddenSeasonalCommand.Execute(item);
                return;
            }

            ResetHideConfirm();
            item.IsHideConfirming = true;
            _hideConfirmItem = item;
            _hideConfirmTimer = new Avalonia.Threading.DispatcherTimer { Interval = HideConfirmTimeout };
            _hideConfirmTimer.Tick += (_, _) => ResetHideConfirm();
            _hideConfirmTimer.Start();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalHideConfirmController.HandleHideBtnTapped failed");
        }
    }

    public void ResetHideConfirm()
    {
        if (_hideConfirmTimer != null)
        {
            _hideConfirmTimer.Stop();
            _hideConfirmTimer = null;
        }
        if (_hideConfirmItem != null)
        {
            _hideConfirmItem.IsHideConfirming = false;
            _hideConfirmItem = null;
        }
    }
}
