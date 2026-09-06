using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Main;

public partial class MainWindowViewModel :
    IRecipient<AnimeCompletedRatingPromptMessage>,
    IRecipient<AnimeRewatchPromptMessage>
{
    public static int[] RatingScores { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    [ObservableProperty]
    private bool _isQuickRatingVisible;

    [ObservableProperty]
    private AnimeEntity? _quickRatingAnime;

    [ObservableProperty]
    private int _selectedRatingScore;

    [ObservableProperty]
    private bool _isRewatchPromptVisible;

    [ObservableProperty]
    private AnimeEntity? _rewatchPromptAnime;

    [ObservableProperty]
    private int _rewatchEpisode = 1;

    [ObservableProperty]
    private string _rewatchPromptText = string.Empty;

    public void Receive(AnimeCompletedRatingPromptMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            QuickRatingAnime = message.Anime;
            if (int.TryParse(message.Anime.Score, out var s) && s >= 1 && s <= 10)
            {
                SelectedRatingScore = s;
            }
            else
            {
                SelectedRatingScore = 0;
            }
            IsQuickRatingVisible = true;
        });
    }

    public void Receive(AnimeRewatchPromptMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RewatchPromptAnime = message.Anime;
            RewatchEpisode = message.Episode;
            var title = message.Anime.Presentation.DisplayTitle;
            RewatchPromptText = string.Format(_localizer.GetLoc("scrobbler.rewatch_prompt.body"), title);
            IsRewatchPromptVisible = true;
        });
    }

    [RelayCommand]
    public async Task SubmitQuickRatingInt(int score)
    {
        if (QuickRatingAnime != null && score >= 1 && score <= 10)
        {
            SelectedRatingScore = score;
            await _progressService.SetScoreAsync(QuickRatingAnime, score);
        }
        CloseQuickRating();
    }

    [RelayCommand]
    public void CloseQuickRating()
    {
        IsQuickRatingVisible = false;
        QuickRatingAnime = null;
    }

    [RelayCommand]
    public async Task ConfirmRewatch()
    {
        if (RewatchPromptAnime != null)
        {
            await _progressService.ConfirmRewatchAsync(RewatchPromptAnime, RewatchEpisode);
        }
        CloseRewatchPrompt();
    }

    [RelayCommand]
    public void DismissRewatchPrompt()
    {
        CloseRewatchPrompt();
    }

    private void CloseRewatchPrompt()
    {
        IsRewatchPromptVisible = false;
        RewatchPromptAnime = null;
    }
}
