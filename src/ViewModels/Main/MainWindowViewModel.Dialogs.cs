using Kiriha.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace Kiriha.ViewModels.Main;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isUpdateDialogOpen;

    [ObservableProperty]
    private UpdateDialogViewModel? _updateDialog;

    [RelayCommand]
    public void NavigateSettings()
    {
        if (IsNavigationBlocked) return;
        EnsureSettingsViewModel();
        IsSettingsOpen = true;
        IsSettingsSelected = true;
    }

    [RelayCommand]
    public void CloseSettings()
    {
        IsSettingsOpen = false;
        IsSettingsSelected = false;
    }

    public void ShowUpdateDialog(bool isDownloaded = false)
    {
        if (IsUpdateDialogOpen) return;
        UpdateDialog = _viewModelFactory.CreateWithArgs<UpdateDialogViewModel>((Action)CloseUpdateDialog, isDownloaded);
        IsUpdateDialogOpen = true;
    }

    [RelayCommand]
    public void CloseUpdateDialog()
    {
        IsUpdateDialogOpen = false;
        UpdateDialog = null;
    }
}
