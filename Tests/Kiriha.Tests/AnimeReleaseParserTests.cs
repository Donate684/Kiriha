using Kiriha.Utils.Parsing;
using Xunit;

namespace Kiriha.Tests;

public sealed class AnimeReleaseParserTests
{
    [Theory]
    [InlineData("О моём перерождении в меч - Эпизод 3", "О моём перерождении в меч", "3")]
    [InlineData("О моём перерождении в меч - Эп 3", "О моём перерождении в меч", "3")]
    [InlineData("Магическая битва 3 серия", "Магическая битва", "3")]
    [InlineData("Магическая битва 3 эпизод", "Магическая битва", "3")]
    public void Parse_CyrillicEpisodeKeywords_ExtractsTitleAndEpisode(
        string input,
        string expectedTitle,
        string expectedEpisode)
    {
        var parsed = AnimeReleaseParser.Parse(input);

        Assert.Equal(expectedTitle, parsed.Title);
        Assert.Equal(expectedEpisode, parsed.Episode);
    }
}
