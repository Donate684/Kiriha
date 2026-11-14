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
        NavigateSettingsTo(-1);
    }

    public void NavigateSettingsTo(int sectionIndex)
    {
        if (IsNavigationBlocked) return;
        var vm = EnsureSettingsViewModel();
        if (sectionIndex >= 0)
        {
            vm.SelectedSectionIndex = sectionIndex;
        }
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

    public bool TryHandleEscapeKey()
    {
        if (IsRewatchPromptVisible)
        {
            DismissRewatchPrompt();
            return true;
        }

        if (IsQuickRatingVisible)
        {
            CloseQuickRating();
            return true;
        }

        if (IsUpdateDialogOpen)
        {
            if (UpdateDialog?.IsDownloadingOrFinished != true)
            {
                CloseUpdateDialog();
                return true;
            }
            return false;
        }

        if (IsSettingsOpen)
        {
            CloseSettings();
            return true;
        }

        if (CurrentPage is Kiriha.ViewModels.Torrents.TorrentsViewModel torrentsVm)
        {
            if (torrentsVm.IsOnlineEpisodeSelectorVisible)
            {
                torrentsVm.CloseOnlineEpisodeSelectorCommand.Execute(null);
                return true;
            }

            if (torrentsVm.IsEpisodeSelectorVisible)
            {
                torrentsVm.CloseEpisodeSelectorCommand.Execute(null);
                return true;
            }
        }

        if (CurrentPage is Kiriha.ViewModels.Analytics.AnalyticsViewModel analyticsVm &&
            analyticsVm.History.IsHistoryPopupOpen)
        {
            analyticsVm.History.CloseHistoryPopup();
            return true;
        }

        return false;
    }
}
