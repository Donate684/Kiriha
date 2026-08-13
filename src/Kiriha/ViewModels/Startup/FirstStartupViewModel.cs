using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data;
using Kiriha.ViewModels.Settings;

namespace Kiriha.ViewModels.Startup;

public partial class FirstStartupViewModel : ViewModelBase
{
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly Kiriha.Core.Abstractions.Services.ILocalizer _localizer;
    private readonly SettingsViewModel _settingsViewModel;

    [ObservableProperty]
    private int _currentStepIndex;

    [ObservableProperty]
    private SetupStep? _currentStep;

    public ObservableCollection<SetupStep> Steps { get; } = new();

    public event Action? SetupCompleted;

    public SettingsViewModel SettingsVm => _settingsViewModel;

    public FirstStartupViewModel(
        Kiriha.Core.Abstractions.Services.ISettingsService settingsService,
        Kiriha.Core.Abstractions.Services.ILocalizer localizer,
        SettingsViewModel settingsViewModel)
    {
        _settingsService = settingsService;
        _localizer = localizer;
        _settingsViewModel = settingsViewModel;

        _settingsViewModel.System.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsSystemViewModel.EnableScrobbler))
            {
                NextStepCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanGoNext));
            }
        };

        _settingsViewModel.Playback.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsPlaybackViewModel.EnabledPlayersCount))
            {
                NextStepCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanGoNext));
            }
        };

        InitializeSteps();
        UpdateCurrentStep();
    }

    private void InitializeSteps()
    {
        var allSteps = new List<SetupStep>
        {
            new SetupStep { Key = "language", TitleKey = "wizard.language.title", SubtitleKey = "wizard.language.subtitle", IconKind = "Translate" },
            new SetupStep { Key = "theme", TitleKey = "wizard.theme.title", SubtitleKey = "wizard.theme.subtitle", IconKind = "Palette" },
            new SetupStep { Key = "mal_login", TitleKey = "wizard.mal.title", SubtitleKey = "wizard.mal.subtitle", IconKind = "AccountSync" },
            new SetupStep { Key = "scrobbler", TitleKey = "wizard.tracking.title", SubtitleKey = "wizard.tracking.instructions", IconKind = "AutoFix" },
            new SetupStep { Key = "system_settings", TitleKey = "wizard.system.title", SubtitleKey = "wizard.system.subtitle", IconKind = "CogOutline" },
            new SetupStep { Key = "advanced_localization", TitleKey = "wizard.advanced.title", SubtitleKey = "wizard.advanced.subtitle", IconKind = "TranslateVariant" }
        };

        foreach (var step in allSteps)
        {
            if (!_settingsService.Current.System.CompletedSetupSteps.Contains(step.Key))
            {
                Steps.Add(step);
            }
        }
    }

    [ObservableProperty]
    private bool _isLastStep;

    public bool CanGoNext => CanNext();
    public bool IsLanguageStep => CurrentStep?.Key == "language";
    public bool IsThemeStep => CurrentStep?.Key == "theme";
    public bool IsMalStep => CurrentStep?.Key == "mal_login";
    public bool IsScrobblerStep => CurrentStep?.Key == "scrobbler";
    public bool IsSystemStep => CurrentStep?.Key == "system_settings";
    public bool IsAdvancedStep => CurrentStep?.Key == "advanced_localization";

    private bool CanNext()
    {
        if (CurrentStep?.Key == "scrobbler")
        {
            if (_settingsViewModel.System.EnableScrobbler && _settingsViewModel.Playback.EnabledPlayersCount == 0)
                return false;
        }
        return true;
    }

    private void UpdateCurrentStep()
    {
        if (CurrentStepIndex < Steps.Count)
        {
            CurrentStep = Steps[CurrentStepIndex];
            IsLastStep = CurrentStepIndex == Steps.Count - 1;
            NextStepCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanGoNext));
            NotifyCurrentStepFlagsChanged();
            OnPropertyChanged(nameof(ProgressText));
        }
        else
        {
            CurrentStep = null;
            IsLastStep = false;
            NotifyCurrentStepFlagsChanged();
            OnPropertyChanged(nameof(ProgressText));
            // If we ran out of steps due to skipping, trigger completion
            SetupCompleted?.Invoke();
        }
    }

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void NextStep()
    {
        if (CurrentStep != null)
        {
            _settingsService.CompleteSetupStep(CurrentStep.Key);

            // AUTO-ENABLE Russian features if 'ru' was chosen in step 1
            if (CurrentStep.Key == "language" && _settingsViewModel.Ui.SelectedLanguage?.Code == "ru")
            {
                _settingsViewModel.Ui.UseRussianTitles = true;
                _settingsViewModel.Ui.UseRussianDescriptions = true;
            }
        }

        if (CurrentStepIndex < Steps.Count - 1)
        {
            CurrentStepIndex++;
            UpdateCurrentStep();
        }
        else
        {
            SetupCompleted?.Invoke();
        }
    }

    public string ProgressText => _localizer.GetLoc("wizard.step_of", CurrentStepIndex + 1, Steps.Count);

    private void NotifyCurrentStepFlagsChanged()
    {
        OnPropertyChanged(nameof(IsLanguageStep));
        OnPropertyChanged(nameof(IsThemeStep));
        OnPropertyChanged(nameof(IsMalStep));
        OnPropertyChanged(nameof(IsScrobblerStep));
        OnPropertyChanged(nameof(IsSystemStep));
        OnPropertyChanged(nameof(IsAdvancedStep));
    }
}
