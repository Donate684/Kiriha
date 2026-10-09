using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeEditViewModel : ObservableObject, IDisposable
{
    private readonly AnimeEntity _originalAnime;
    private readonly AnimeEntity _anime;
    private readonly IAnimeListActionService _listActionService;
    private readonly System.ComponentModel.PropertyChangedEventHandler _animePropertyChanged;
    private bool _isRemoving;

    [ObservableProperty]
    private bool _isDeleteConfirmationVisible;

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
        IAnimeListActionService listActionService)
    {
        _originalAnime = originalAnime;
        _anime = cloneAnime;
        _listActionService = listActionService;

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
        var targetStatus = AppConstants.AiringStatus.IsNotYetAired(_anime.StatusDetailed)
            ? UserAnimeStatus.PlanToWatch
            : UserAnimeStatus.Watching;

        _anime.Status = targetStatus;
        if (_anime.MediaKind == MediaKind.Anime)
            _anime.Progress = 0;
        else
            _anime.ChaptersRead = 0;

        await _listActionService.AddToListAsync(_anime, targetStatus, 0);

        _anime.CopyTo(_originalAnime);
        OnPropertyChanged(nameof(IsInList));
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }

    public bool HasChanges
    {
        get
        {
            if (_originalAnime is null || _anime is null) return false;

            var currentScore = GetCleanScore(_anime.Score);
            var origScore = GetCleanScore(_originalAnime.Score);

            return _originalAnime.Status != _anime.Status ||
                   _originalAnime.Progress != _anime.Progress ||
                   _originalAnime.ChaptersRead != _anime.ChaptersRead ||
                   _originalAnime.VolumesRead != _anime.VolumesRead ||
                   !MemoryExtensions.SequenceEqual(origScore, currentScore) ||
                   _originalAnime.IsRewatching != _anime.IsRewatching ||
                   _originalAnime.RewatchCount != _anime.RewatchCount ||
                   _originalAnime.Notes != _anime.Notes ||
                   _originalAnime.DateStarted != _anime.DateStarted ||
                   _originalAnime.DateCompleted != _anime.DateCompleted;
        }
    }

    private static ReadOnlySpan<char> GetCleanScore(string? score)
    {
        if (string.IsNullOrEmpty(score) || score == "-") return ReadOnlySpan<char>.Empty;
        var span = score.AsSpan().Trim();
        int idx = span.IndexOf(' ');
        return idx >= 0 ? span[..idx] : span;
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task Save(object? window)
    {
        if (_isRemoving) return;

        if (HasChanges)
        {
            await _listActionService.SaveAnimeAsync(_originalAnime, _anime);
        }

        if (window is Avalonia.Controls.Window w) w.Close(true);
    }

    [RelayCommand]
    public async Task RemoveFromList(object window)
    {
        if (!IsDeleteConfirmationVisible)
        {
            IsDeleteConfirmationVisible = true;
            return;
        }

        _isRemoving = true;
        await _listActionService.RemoveFromListAsync(_originalAnime.Id);

        if (window is Avalonia.Controls.Window w) w.Close(true);
    }

    public void Dispose()
    {
        _anime.PropertyChanged -= _animePropertyChanged;
    }
}

