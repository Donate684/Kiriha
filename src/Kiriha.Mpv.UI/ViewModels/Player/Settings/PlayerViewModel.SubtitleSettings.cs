using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Constants;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private string _preferredSubtitleLanguages = "Russian,rus,ru";
    [ObservableProperty] private bool _subtitleStyleOverrideEnabled = false;
    [ObservableProperty] private string _subtitleFont = AppConstants.Player.DefaultSubtitleFont;
    [ObservableProperty] private double _subtitleFontSize = 55;
    [ObservableProperty] private string _subtitleColor = "#FFFFFF";
    [ObservableProperty] private string _subtitleBorderColor = "#000000";
    [ObservableProperty] private string _subtitleShadowColor = "#000000";
    [ObservableProperty] private double _subtitleBorderSize = 3.0;
    [ObservableProperty] private double _subtitleShadowOffset = 1.0;
    [ObservableProperty] private string _subtitleAlignY = "bottom";
    [ObservableProperty] private string _subtitleAlignX = "center";
    [ObservableProperty] private int _subtitleMarginY = 40;
    [ObservableProperty] private bool _subtitleScaleByWindow = true;
}
