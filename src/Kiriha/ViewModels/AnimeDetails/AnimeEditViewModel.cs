using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Serilog;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeEditViewModel : ObservableObject, IDisposable
{
    private readonly AnimeEntity _originalAnime;
    private readonly AnimeEntity _anime;
    private readonly IAnimeListActionService _listActionService;
    private readonly System.ComponentModel.PropertyChangedEventHandler _animePropertyChanged;
    private AnimeEditFields _baseline;
    internal long EntryGeneration { get; private set; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDeleteConfirmationVisible;

    [ObservableProperty]
    private bool _hasSaveError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntryConflict))]
    [NotifyPropertyChangedFor(nameof(HasFieldConflict))]
    private AnimeEditConflict _conflict;

    public bool HasEntryConflict => Conflict == AnimeEditConflict.EntryChanged;
    public bool HasFieldConflict => Conflict == AnimeEditConflict.FieldsChanged;

    public bool IsInList => _anime.Status != UserAnimeStatus.None;

    public IEnumerable<UserAnimeStatus> AvailableStatuses =>
    [
        UserAnimeStatus.Watching,
        UserAnimeStatus.Completed,
        UserAnimeStatus.OnHold,
        UserAnimeStatus.Dropped,
        UserAnimeStatus.PlanToWatch
    ];

    public IEnumerable<RatingOption> AvailableScores =>
    [
        RatingHelper.GetRatingOption("-"),
        RatingHelper.GetRatingOption("10"),
        RatingHelper.GetRatingOption("9"),
        RatingHelper.GetRatingOption("8"),
        RatingHelper.GetRatingOption("7"),
        RatingHelper.GetRatingOption("6"),
        RatingHelper.GetRatingOption("5"),
        RatingHelper.GetRatingOption("4"),
        RatingHelper.GetRatingOption("3"),
        RatingHelper.GetRatingOption("2"),
        RatingHelper.GetRatingOption("1")
    ];

    public AnimeEditViewModel(
        AnimeEntity originalAnime,
        AnimeEntity cloneAnime,
        IAnimeListActionService listActionService,
        IAnimeRepository? repository = null)
    {
        _originalAnime = originalAnime;
        _anime = cloneAnime;
        _listActionService = listActionService;
        _baseline = AnimeEditFields.From(cloneAnime);
        EntryGeneration = repository?.GetEntryGeneration(originalAnime.Id, originalAnime.MediaKind) ?? 0;

        _animePropertyChanged = (s, e) =>
        {
            if (e.PropertyName == nameof(AnimeEntity.Status))
                OnPropertyChanged(nameof(IsInList));

            if (e.PropertyName == nameof(AnimeEntity.TotalEpisodes))
                OnPropertyChanged(nameof(MaxEpisodes));

            if (e.PropertyName == nameof(AnimeEntity.Chapters))
                OnPropertyChanged(nameof(MaxChapters));

            if (e.PropertyName == nameof(AnimeEntity.Volumes))
                OnPropertyChanged(nameof(MaxVolumes));

            OnPropertyChanged(nameof(HasChanges));
            SaveCommand.NotifyCanExecuteChanged();
        };
        _anime.PropertyChanged += _animePropertyChanged;
    }

    internal static AnimeEntity ResolveEntry(IAnimeRepository repository, AnimeEntity item)
    {
        var current = repository.FindById(item.Id, item.MediaKind);
        if (current != null) return current;
        if (repository.GetEntryGeneration(item.Id, item.MediaKind) == 0) return item;

        // Navigation may still hold a reference to an entry removed from the library.
        var untracked = item.Clone();
        new AnimeEditFields(UserAnimeStatus.None, 0, 0, 0, "-", null, false, 0, null, null).ApplyTo(untracked);
        return untracked;
    }

    public int MaxEpisodes => _anime.TotalEpisodes > 0 ? _anime.TotalEpisodes : 9999;
    public int MaxChapters => _anime.Chapters > 0 ? _anime.Chapters : 9999;
    public int MaxVolumes => _anime.Volumes > 0 ? _anime.Volumes : 9999;

    [RelayCommand]
    private void DecrementProgress()
    {
        if (_anime.MediaKind != MediaKind.Anime)
        {
            if (_anime.ChaptersRead > 0)
                _anime.ChaptersRead--;
        }
        else
        {
            if (_anime.Progress > 0)
                _anime.Progress--;
        }
    }

    [RelayCommand]
    private void DecrementVolumes()
    {
        if (_anime.VolumesRead > 0)
            _anime.VolumesRead--;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteConfirmationVisible = false;
        HasSaveError = false;
        Conflict = AnimeEditConflict.None;
    }

    [RelayCommand]
    private void IncrementProgress()
    {
        if (_anime.MediaKind != MediaKind.Anime)
        {
            if (_anime.ChaptersRead < _anime.Chapters || _anime.Chapters == 0)
                _anime.ChaptersRead++;
        }
        else
        {
            if (_anime.Progress < _anime.TotalEpisodes || _anime.TotalEpisodes == 0)
                _anime.Progress++;
        }

        if (_anime.Status == UserAnimeStatus.PlanToWatch ||
            _anime.Status == UserAnimeStatus.OnHold ||
            _anime.Status == UserAnimeStatus.Dropped)
        {
            _anime.Status = UserAnimeStatus.Watching;
        }
    }

    [RelayCommand]
    private void IncrementVolumes()
    {
        if (_anime.VolumesRead < _anime.Volumes || _anime.Volumes == 0)
            _anime.VolumesRead++;

        if (_anime.Status == UserAnimeStatus.PlanToWatch ||
            _anime.Status == UserAnimeStatus.OnHold ||
            _anime.Status == UserAnimeStatus.Dropped)
        {
            _anime.Status = UserAnimeStatus.Watching;
        }
    }

    [RelayCommand]
    private void SetStartDateToToday()
    {
        _anime.DateStarted = DateTime.Today;
    }

    [RelayCommand]
    private void SetStartDateToYesterday()
    {
        _anime.DateStarted = DateTime.Today.AddDays(-1);
    }

    [RelayCommand]
    private void ClearStartDate()
    {
        _anime.DateStarted = null;
    }

    [RelayCommand]
    private void SetEndDateToToday()
    {
        _anime.DateCompleted = DateTime.Today;
    }

    [RelayCommand]
    private void SetEndDateToYesterday()
    {
        _anime.DateCompleted = DateTime.Today.AddDays(-1);
    }

    [RelayCommand]
    private void ClearEndDate()
    {
        _anime.DateCompleted = null;
    }

    [RelayCommand]
    private async Task AddToList()
    {
        if (IsBusy) return;

        var targetStatus = AppConstants.AiringStatus.IsNotYetAired(_anime.StatusDetailed)
            ? UserAnimeStatus.PlanToWatch
            : UserAnimeStatus.Watching;

        var submitted = AnimeEditFields.From(_anime);
        var candidate = _anime.Clone();
        if (candidate.MediaKind == MediaKind.Anime)
            candidate.Progress = 0;
        else
            candidate.ChaptersRead = 0;

        IsBusy = true;
        try
        {
            var result = await _listActionService.AddFromEditorAsync(candidate, targetStatus, EntryGeneration);
            SetResult(result);
            if (result.CommittedEdit is { } committed)
                ApplyCommittedFields(submitted, committed.Fields, committed.Generation);
        }
        catch (Exception ex)
        {
            SetFailure(ex, "add");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool HasChanges => AnimeEditFields.From(_anime) != _baseline;

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task Save(object? window)
    {
        if (!await TrySaveAsync() || HasChanges) return;

        if (window is Avalonia.Controls.Window w) w.Close(true);
    }

    internal async Task<bool> TrySaveAsync()
    {
        if (IsBusy) return false;
        if (!HasChanges) return true;

        var submitted = AnimeEditFields.From(_anime);
        IsBusy = true;
        try
        {
            var result = await _listActionService.SaveEditorChangesAsync(
                _originalAnime.Id, _originalAnime.MediaKind, EntryGeneration, _baseline, submitted);
            SetResult(result);
            if (result.CommittedEdit is { } committed)
                ApplyCommittedFields(submitted, committed.Fields, committed.Generation);
            return result.Success;
        }
        catch (Exception ex)
        {
            SetFailure(ex, "save");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RemoveFromList(object window)
    {
        if (IsBusy) return;

        if (!IsDeleteConfirmationVisible)
        {
            IsDeleteConfirmationVisible = true;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _listActionService.RemoveFromEditorAsync(
                _originalAnime.Id, _originalAnime.MediaKind, EntryGeneration);
            SetResult(result);
            if (result.Success && window is Avalonia.Controls.Window w)
                w.Close(true);
        }
        catch (Exception ex)
        {
            SetFailure(ex, "remove");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal void ApplyImportedFields(AnimeEditFields fields, long generation)
    {
        ApplyCommittedFields(_baseline, fields, generation);
    }

    private void ApplyCommittedFields(AnimeEditFields submitted, AnimeEditFields committed, long generation)
    {
        // Keep any input received after the operation started.
        submitted.TryMerge(AnimeEditFields.From(_anime), committed, out var updatedDraft);
        _baseline = committed;
        EntryGeneration = generation;
        updatedDraft.ApplyTo(_anime);
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void SetResult(ListActionResult result)
    {
        Conflict = result.Conflict;
        HasSaveError = !result.Success && Conflict == AnimeEditConflict.None;
    }

    private void SetFailure(Exception error, string operation)
    {
        Log.Error(error, "Failed to {Operation} library entry {Id} from the editor", operation, _originalAnime.Id);
        Conflict = AnimeEditConflict.None;
        HasSaveError = true;
    }

    public void Dispose()
    {
        _anime.PropertyChanged -= _animePropertyChanged;
    }
}

