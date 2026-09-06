using System;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services;
using Kiriha.Core.Tracking.Api;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeEditViewModel : ObservableObject
{
    private readonly AnimeEntity _originalAnime;
    private readonly AnimeEntity _anime;
    private readonly ISyncManager _syncManager;
    private readonly IAnimeRepository _animeRepo;
    private readonly IProgressUpdateService _animeProgressService;
    private readonly IHistoryService _historyService;
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
        ISyncManager syncManager,
        IAnimeRepository animeRepo,
        IProgressUpdateService animeProgressService,
        IHistoryService historyService)
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
    private void AddToList()
    {
        _anime.Status = UserAnimeStatus.Watching;
    }

    public bool HasChanges
    {
        get
        {
            if (_originalAnime == null || _anime == null) return false;

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

        bool markedAsDropped = _originalAnime.Status != UserAnimeStatus.Dropped && _anime.Status == UserAnimeStatus.Dropped;
        bool markedAsCompleted = _originalAnime.Status != UserAnimeStatus.Completed && _anime.Status == UserAnimeStatus.Completed;

        string rawScore = _anime.Score;
        if (rawScore != "-" && rawScore.Contains(' '))
        {
            _anime.Score = rawScore.Substring(0, rawScore.IndexOf(' '));
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

