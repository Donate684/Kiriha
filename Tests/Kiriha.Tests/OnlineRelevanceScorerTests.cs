using Kiriha.ViewModels.Torrents;

namespace Kiriha.Tests;

public class OnlineRelevanceScorerTests
{
    [Fact]
    public void Score_ExactMatch_ReturnsMaximumScore()
    {
        var score1 = OnlineRelevanceScorer.Score("Мой сэмпай раздражает!", "Мой сэмпай раздражает!");
        var score2 = OnlineRelevanceScorer.Score("Мой сэмпай раздражает!", "Мой сэмпай раздражает");

        Assert.Equal(10000, score1);
        Assert.Equal(10000, score2);
    }

    [Fact]
    public void Score_ExactMatchFromAnyProvider_RanksAbovePartialMatches()
    {
        const string query = "Мой сэмпай раздражает!";
        var exactMatchAniLibria = OnlineRelevanceScorer.Score(query, "Мой сэмпай раздражает!");
        var exactMatchAnimeLib = OnlineRelevanceScorer.Score(query, "Мой сэмпай раздражает!");
        var partial1 = OnlineRelevanceScorer.Score(query, "Мой сосед — бог разрушения!");
        var partial2 = OnlineRelevanceScorer.Score(query, "Мой домашний герой");
        var partial3 = OnlineRelevanceScorer.Score(query, "Почему все забыли мой мир?");

        Assert.Equal(10000, exactMatchAniLibria);
        Assert.Equal(10000, exactMatchAnimeLib);
        Assert.True(exactMatchAnimeLib > partial1, $"AnimeLib ({exactMatchAnimeLib}) should rank higher than partial ({partial1})");
        Assert.True(exactMatchAnimeLib > partial2, $"AnimeLib ({exactMatchAnimeLib}) should rank higher than partial ({partial2})");
        Assert.True(exactMatchAnimeLib > partial3, $"AnimeLib ({exactMatchAnimeLib}) should rank higher than partial ({partial3})");
    }

    [Fact]
    public void ComputeMaxRelevance_MatchesEitherRussianOrRomajiTarget()
    {
        var targets = new[] { "Мой сэмпай раздражает!", "Senpai ga Uzai Kouhai no Hanashi" };

        var russianScore = OnlineRelevanceScorer.ComputeMaxRelevance(targets, "Мой сэмпай раздражает!");
        var romajiScore = OnlineRelevanceScorer.ComputeMaxRelevance(targets, "Senpai ga Uzai Kouhai no Hanashi");
        var irrelevantScore = OnlineRelevanceScorer.ComputeMaxRelevance(targets, "Мой сосед — бог разрушения!");

        Assert.Equal(10000, russianScore);
        Assert.Equal(10000, romajiScore);
        Assert.True(russianScore > irrelevantScore);
        Assert.True(romajiScore > irrelevantScore);
    }

    [Fact]
    public void Score_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0, OnlineRelevanceScorer.Score("", "Candidate"));
        Assert.Equal(0, OnlineRelevanceScorer.Score("Query", ""));
        Assert.Equal(0, OnlineRelevanceScorer.Score("   ", "   "));
    }
}
