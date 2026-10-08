using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.ViewModels.Dialogs;

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
        if (_settingsViewModel?.Auth.IsSwitchSyncing == true)
        {
            return;
        }

        if (_settingsViewModel?.Auth.IsAuthDialogOpen == true)
        {
            _settingsViewModel.Auth.CancelAuth();
        }

        if (_settingsViewModel?.Auth.IsSwitchDialogOpen == true)
        {
            _settingsViewModel.Auth.CancelPrimarySwitch();
        }

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
