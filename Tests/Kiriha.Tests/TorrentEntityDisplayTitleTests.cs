using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Tests;

public class TorrentEntityDisplayTitleTests
{
    [Theory]
    [InlineData("[Erai-raws] Senpai ga Uzai Kouhai no Hanashi - 01 ~ 12 [480p][BATCH][Multiple Subtitle] [ENG]", "Erai-raws", "Senpai ga Uzai Kouhai no Hanashi - 01 ~ 12 [480p][BATCH][Multiple Subtitle] [ENG]")]
    [InlineData("[Erai-Raws] Jujutsu Kaisen - 01 [1080p]", "Erai-raws", "Jujutsu Kaisen - 01 [1080p]")]
    [InlineData("[Erai Raws] Bleach - 01 [720p]", "Erai-raws", "Bleach - 01 [720p]")]
    [InlineData("[Erai_raws] Naruto - 01 [1080p]", "Erai-raws", "Naruto - 01 [1080p]")]
    [InlineData("(Erai-raws) One Piece - 1000", "Erai-raws", "One Piece - 1000")]
    [InlineData("[SubsPlease] Frieren - Beyond Journey's End - 01 (1080p) [12345678].mkv", "SubsPlease", "Frieren - Beyond Journey's End - 01 (1080p) [12345678].mkv")]
    [InlineData("No Group Title - 01 [1080p]", "", "No Group Title - 01 [1080p]")]
    public void DisplayTitle_StripsReleaseGroupCorrectly(string title, string releaseGroup, string expectedDisplayTitle)
    {
        var entity = new TorrentEntity
        {
            Title = title,
            ReleaseGroup = releaseGroup
        };

        Assert.Equal(expectedDisplayTitle, entity.DisplayTitle);
    }
}
