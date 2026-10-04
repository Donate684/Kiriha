using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Models;
using Kiriha.ViewModels.Settings;

namespace Kiriha.ViewModels.Startup;

public partial class FirstStartupViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizer _localizer;
    private readonly SettingsViewModel _settingsViewModel;

    [ObservableProperty]
    private int _currentStepIndex;

    [ObservableProperty]
    private SetupStep? _currentStep;

    public ObservableCollection<SetupStep> Steps { get; } = new();

    public event Action? SetupCompleted;

    public SettingsViewModel SettingsVm => _settingsViewModel;

    public FirstStartupViewModel(
        ISettingsService settingsService,
        ILocalizer localizer,
        SettingsViewModel settingsViewModel)
    {
        _settingsService = settingsService;
        _localizer = localizer;
        _settingsViewModel = settingsViewModel;

        InitializeSteps();
        UpdateCurrentStep();
    }

    private void InitializeSteps()
    {
        var allSteps = new List<SetupStep>
        {
            new SetupStep { Key = "language", TitleKey = "wizard.language.title", SubtitleKey = "wizard.language.subtitle", IconKind = "Translate" }
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
    public bool IsThemeStep => false;
    public bool IsMalStep => false;
    public bool IsScrobblerStep => false;
    public bool IsSystemStep => false;
    public bool IsAdvancedStep => false;

    private bool CanNext() => true;

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

    public string ProgressText => Steps.Count > 1 ? _localizer.GetLoc("wizard.step_of", CurrentStepIndex + 1, Steps.Count) : string.Empty;

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
