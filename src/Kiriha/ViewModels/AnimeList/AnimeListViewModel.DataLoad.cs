using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Utils.Async;
using Kiriha.Core;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    private object[]? _availableScores;
    public object[] AvailableScores => _availableScores ??= CreateAvailableScores();

    private static object[] CreateAvailableScores() =>
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

    private string GetLoc(string key) => UIUtils.GetLoc(key);

    public void RefreshAvailableScores()
    {
        _availableScores = CreateAvailableScores();
        OnPropertyChanged(nameof(AvailableScores));
    }

    public void RefreshLocalization()
    {
        RefreshAvailableScores();
        UpdateCountsAsync().SafeFireAndForget("RefreshLocalization");
        foreach (var item in AnimeItems) item.RefreshMetadata();
    }
}
