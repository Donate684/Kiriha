using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kiriha.ViewModels.History;

namespace Kiriha.Views.History;

public partial class HistoryView : UserControl
{
    // Reveal-on-load ÐºÐ°ÑÐºÐ°Ð´ Ð´Ð»Ñ ÑÑ‚Ñ€Ð¾Ðº Ñ‚Ð°Ð¹Ð¼Ð»Ð°Ð¹Ð½Ð°.
    private DateTime _lastRevealEvent = DateTime.MinValue;
    private int _revealStaggerIndex;
    private const int RevealStaggerStepMs = 30;
    private const int RevealStaggerIdleResetMs = 140;

    public HistoryView()
    {
        InitializeComponent();
    }

    private void OnHistoryRowLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Border card) return;

        card.Classes.Remove("shown");

        var now = DateTime.UtcNow;
        if ((now - _lastRevealEvent).TotalMilliseconds > RevealStaggerIdleResetMs)
            _revealStaggerIndex = 0;
        _lastRevealEvent = now;

        var delay = TimeSpan.FromMilliseconds(_revealStaggerIndex++ * RevealStaggerStepMs);
        DispatcherTimer.RunOnce(() => card.Classes.Add("shown"), delay);
    }

    private void HistoryItem_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is HistoryEntryVm entry)
        {
            if (DataContext is HistoryViewModel vm)
            {
                vm.OpenAnimeDetailsCommand.Execute(entry);
            }
        }
    }
}

