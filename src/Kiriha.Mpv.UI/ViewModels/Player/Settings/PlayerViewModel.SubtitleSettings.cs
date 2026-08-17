using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private string _preferredSubtitleLanguages = "Russian,rus,ru";
    [ObservableProperty] private bool _subtitleStyleOverrideEnabled = false;
    [ObservableProperty] private string _subtitleFont = "Candara Bold";
    [ObservableProperty] private double _subtitleFontSize = 60;
    [ObservableProperty] private string _subtitleColor = "#FFFFFF";
    [ObservableProperty] private string _subtitleBorderColor = "#000000";
    [ObservableProperty] private string _subtitleShadowColor = "#000000";
    [ObservableProperty] private double _subtitleBorderSize = 3.8;
    [ObservableProperty] private double _subtitleShadowOffset = 1.5;
    [ObservableProperty] private string _subtitleAlignY = "bottom";
    [ObservableProperty] private string _subtitleAlignX = "center";
    [ObservableProperty] private int _subtitleMarginY = 35;
    [ObservableProperty] private bool _subtitleScaleByWindow = true;
}
