using Kiriha.Core.Tracking.Integration;
using System;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services;
using Kiriha.Core.Tracking.Api;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeEditViewModel : ObservableObject
{
    private readonly AnimeEntity _originalAnime;
    private readonly AnimeEntity _anime;
    private readonly SyncManager _syncManager;
    private readonly AnimeRepository _animeRepo;
    private readonly AnimeProgressService _animeProgressService;
    private readonly HistoryService _historyService;
    private bool _isRemoving;

    [ObservableProperty]
    private bool _isDeleteConfirmationVisible;

    public bool IsInList => _anime.Status != UserAnimeStatus.None;

    public IEnumerable<UserAnimeStatus> AvailableStatuses => new[]
    {
        UserAnimeStatus.Watching,
        UserAnimeStatus.Completed,
        UserAnimeStatus.OnHold,
        UserAnimeStatus.Dropped,
        UserAnimeStatus.PlanToWatch
    };

    public IEnumerable<RatingOption> AvailableScores => new[]
    {
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
    };

    public AnimeEditViewModel(
        AnimeEntity originalAnime,
        AnimeEntity cloneAnime,
        SyncManager syncManager,
        AnimeRepository animeRepo,
        AnimeProgressService animeProgressService,
        HistoryService historyService)
    {
        _originalAnime = originalAnime;
        _anime = cloneAnime;
        _syncManager = syncManager;
        _animeRepo = animeRepo;
        _animeProgressService = animeProgressService;
        _historyService = historyService;

        _anime.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AnimeEntity.Status))
                OnPropertyChanged(nameof(IsInList));

            OnPropertyChanged(nameof(HasChanges));
            SaveCommand.NotifyCanExecuteChanged();
        };
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
    }

    [RelayCommand]
    private void IncrementVolumes()
    {
        if (_anime.VolumesRead < _anime.Volumes || _anime.Volumes == 0)
            _anime.VolumesRead++;
    }

    [RelayCommand]
    private void SetStartDateToToday()
    {
        _anime.DateStarted = DateTime.UtcNow;
    }

    [RelayCommand]
    private void SetEndDateToToday()
    {
        _anime.DateCompleted = DateTime.UtcNow;
    }

    [RelayCommand]
    private void AddToList()
    {
        _anime.Status = UserAnimeStatus.Watching;
    }

    public bool HasChanges
    {
        get
        {
            if (_originalAnime == null || _anime == null) return false;

            string currentScore = _anime.Score ?? "";
            if (currentScore != "-" && currentScore.Contains(" "))
                currentScore = currentScore.Split(' ')[0];

            string origScore = _originalAnime.Score ?? "";
            if (origScore != "-" && origScore.Contains(" "))
                origScore = origScore.Split(' ')[0];

            return _originalAnime.Status != _anime.Status ||
                   _originalAnime.Progress != _anime.Progress ||
                   _originalAnime.ChaptersRead != _anime.ChaptersRead ||
                   _originalAnime.VolumesRead != _anime.VolumesRead ||
                   origScore != currentScore ||
                   _originalAnime.IsRewatching != _anime.IsRewatching ||
                   _originalAnime.RewatchCount != _anime.RewatchCount ||
                   _originalAnime.Notes != _anime.Notes ||
                   _originalAnime.DateStarted != _anime.DateStarted ||
                   _originalAnime.DateCompleted != _anime.DateCompleted;
        }
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task Save(object? window)
    {
        if (_isRemoving) return;

        bool markedAsDropped = _originalAnime.Status != UserAnimeStatus.Dropped && _anime.Status == UserAnimeStatus.Dropped;
        bool markedAsCompleted = _originalAnime.Status != UserAnimeStatus.Completed && _anime.Status == UserAnimeStatus.Completed;

        string rawScore = _anime.Score;
        if (rawScore != "-" && rawScore.Contains(" "))
        {
            _anime.Score = rawScore.Split(' ')[0];
        }

        bool scoreChanged = _originalAnime.Score != _anime.Score && _anime.Score != "-" && !string.IsNullOrEmpty(_anime.Score);
        bool hasChanges = HasChanges;

        _anime.CopyTo(_originalAnime);

        if (_originalAnime.Status != UserAnimeStatus.None)
        {
            await _animeRepo.AddOrUpdateAnimeAsync(_originalAnime);

            if (markedAsDropped)
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "Dropped");
            if (markedAsCompleted)
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "Completed");
            if (scoreChanged)
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "ScoreSet", _originalAnime.Score);

            if (hasChanges)
                await _syncManager.EnqueueFullUpdateAsync(_originalAnime);

            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
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
        await _animeProgressService.RemoveAnimeAsync(_originalAnime.Id);

        WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());

        if (window is Avalonia.Controls.Window w) w.Close(true);
    }
}
