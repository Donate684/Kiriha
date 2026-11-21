using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.ViewModels.Main;

public partial class MainWindowViewModel :
    IRecipient<AnimeCompletedRatingPromptMessage>,
    IRecipient<AnimeRewatchPromptMessage>
{
    public static int[] RatingScores { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private AnimeCompletedRatingPromptMessage? _quickRatingPrompt;
    private AnimeRewatchPromptMessage? _rewatchPrompt;

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

    public void Receive(AnimeCompletedRatingPromptMessage message) =>
        Dispatcher.UIThread.Post(() => ShowQuickRating(message));

    public void Receive(AnimeRewatchPromptMessage message) =>
        Dispatcher.UIThread.Post(() => ShowRewatchPrompt(message));

    internal void ShowQuickRating(AnimeCompletedRatingPromptMessage message)
    {
        _quickRatingPrompt = message;
        QuickRatingAnime = message.Anime;
        SelectedRatingScore = message.SuggestedScore is >= 1 and <= 10 ? message.SuggestedScore.Value : 0;
        IsQuickRatingVisible = true;
    }

    internal void ShowRewatchPrompt(AnimeRewatchPromptMessage message)
    {
        _rewatchPrompt = message;
        RewatchPromptAnime = message.Anime;
        RewatchEpisode = message.Episode;
        var title = message.Anime.Presentation.DisplayTitle;
        RewatchPromptText = string.Format(_localizer.GetLoc("scrobbler.rewatch_prompt.body"), title);
        IsRewatchPromptVisible = true;
    }

    [RelayCommand]
    public async Task SubmitQuickRatingInt(int score)
    {
        var item = QuickRatingAnime;
        var prompt = _quickRatingPrompt;
        if (item != null && score is >= 1 and <= 10)
        {
            SelectedRatingScore = score;
            try
            {
                if (prompt?.Target is { } target)
                    await _progressService.SetScoreAsync(target, prompt.ExpectedScore, score);
                else
                    await _progressService.SetScoreAsync(item, score);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save the quick rating");
                return;
            }
        }
        if (ReferenceEquals(QuickRatingAnime, item) && ReferenceEquals(_quickRatingPrompt, prompt))
            CloseQuickRating();
    }

    [RelayCommand]
    public void CloseQuickRating()
    {
        IsQuickRatingVisible = false;
        QuickRatingAnime = null;
        _quickRatingPrompt = null;
    }

    [RelayCommand]
    public async Task ConfirmRewatch()
    {
        var item = RewatchPromptAnime;
        var prompt = _rewatchPrompt;
        if (item != null)
        {
            try
            {
                if (prompt?.Target is { } target)
                    await _progressService.ConfirmRewatchAsync(target, prompt.ExpectedRewatchCount, prompt.Episode);
                else
                    await _progressService.ConfirmRewatchAsync(item, RewatchEpisode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save the rewatch");
                return;
            }
        }
        if (ReferenceEquals(RewatchPromptAnime, item) && ReferenceEquals(_rewatchPrompt, prompt))
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
        _rewatchPrompt = null;
    }
}
