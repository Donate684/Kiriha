using System;
using Kiriha.Core.Tracking.Services.Api;
using Xunit;

namespace Kiriha.Tests;

public class MalHistoryParserTests
{
    [Fact]
    public void Parse_ReturnsNull_WhenNotLoggedIn()
    {
        var html = "Not logged in";
        var result = MalHistoryParser.Parse(html);
        Assert.Null(result);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenHtmlIsEmpty()
    {
        var result = MalHistoryParser.Parse("");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_SingleSession_ExtractsCorrectStartAndEndDate()
    {
        var html = """
            <div id="thickbox">
            Ep 12, watched on 09/01/2023 at 20:15 <a href="#">Remove</a><br>
            Ep 11, watched on 08/25/2023 at 19:30 <a href="#">Remove</a><br>
            Ep 2, watched on 07/15/2023 at 14:00 <a href="#">Remove</a><br>
            Ep 1, watched on 07/08/2023 at 12:00 <a href="#">Remove</a><br>
            </div>
            """;

        var result = MalHistoryParser.Parse(html);

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalSessions);
        Assert.Equal(new DateTime(2023, 7, 8), result.LatestStartDate);
        Assert.Equal(new DateTime(2023, 9, 1), result.LatestEndDate);
    }

    [Fact]
    public void Parse_MultipleSessions_SelectsLatestRewatchSession()
    {
        // Sample reconstructed from the user's screenshot of Tsubasa Chronicle
        var html = """
            Tsubasa Chronicle Episode Details
            Ep 26, watched on 07/31/2022 at 22:51 Remove
            Ep 25, watched on 07/30/2022 at 23:00 Remove
            Ep 24, watched on 07/30/2022 at 22:37 Remove
            Ep 23, watched on 07/30/2022 at 22:16 Remove
            Ep 22, watched on 07/30/2022 at 21:54 Remove
            Ep 21, watched on 07/30/2022 at 21:32 Remove
            Ep 20, watched on 07/26/2022 at 21:17 Remove
            Ep 19, watched on 07/26/2022 at 20:56 Remove
            Ep 18, watched on 07/26/2022 at 20:31 Remove
            Ep 17, watched on 07/25/2022 at 22:42 Remove
            Ep 16, watched on 07/25/2022 at 22:18 Remove
            Ep 15, watched on 07/25/2022 at 21:56 Remove
            Ep 14, watched on 07/24/2022 at 22:52 Remove
            Ep 13, watched on 07/24/2022 at 22:30 Remove
            Ep 12, watched on 07/24/2022 at 22:07 Remove
            Ep 11, watched on 07/24/2022 at 21:47 Remove
            Ep 10, watched on 07/24/2022 at 21:26 Remove
            Ep 9, watched on 07/24/2022 at 21:03 Remove
            Ep 8, watched on 07/24/2022 at 20:41 Remove
            Ep 7, watched on 07/24/2022 at 20:18 Remove
            Ep 6, watched on 07/24/2022 at 19:56 Remove
            Ep 5, watched on 07/24/2022 at 19:31 Remove
            Ep 4, watched on 07/23/2022 at 23:18 Remove
            Ep 3, watched on 07/23/2022 at 22:56 Remove
            Ep 2, watched on 07/22/2022 at 23:49 Remove
            Ep 1, watched on 07/22/2022 at 23:49 Remove
            Ep 26, watched on 07/20/2012 at 21:08 Remove
            Ep 23, watched on 07/20/2012 at 17:49 Remove
            Ep 19, watched on 07/19/2012 at 22:41 Remove
            Ep 17, watched on 07/16/2012 at 16:26 Remove
            Ep 13, watched on 07/02/2012 at 17:11 Remove
            Ep 8, watched on 07/01/2012 at 21:10 Remove
            Ep 2, watched on 06/30/2012 at 23:11 Remove
            Ep 1, watched on 06/05/2012 at 23:06 Remove
            """;

        var result = MalHistoryParser.Parse(html);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalSessions);

        // Crucial requirement: Must pick the LATEST rewatch session (2022) instead of the 2012 run!
        Assert.Equal(new DateTime(2022, 7, 22), result.LatestStartDate);
        Assert.Equal(new DateTime(2022, 7, 31), result.LatestEndDate);

        // Also verify the earlier session was preserved
        Assert.Equal(new DateTime(2012, 6, 5), result.Sessions[1].StartDate);
        Assert.Equal(new DateTime(2012, 7, 20), result.Sessions[1].EndDate);
    }

    [Fact]
    public void Parse_SessionWithMinorEpisodeJitter_KeepsSingleSession()
    {
        // Mai-Otome real data where user had minor jitter (ep 4 at 22:08, ep 5 at 22:07)
        var html = """
            Mai-Otome Episode Details
            Ep 26, watched on 01/25/2014 at 00:14 Remove
            Ep 25, watched on 01/24/2014 at 22:17 Remove
            Ep 5, watched on 01/15/2014 at 00:18 Remove
            Ep 4, watched on 01/14/2014 at 22:08 Remove
            Ep 5, watched on 01/14/2014 at 22:07 Remove
            Ep 2, watched on 12/10/2013 at 23:55 Remove
            """;

        var result = MalHistoryParser.Parse(html);

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalSessions);
        Assert.Equal(new DateTime(2013, 12, 10), result.LatestStartDate);
        Assert.Equal(new DateTime(2014, 1, 25), result.LatestEndDate);
    }

    [Fact]
    public void Parse_MangaHistory_ExtractsCorrectStartAndEndDate()
    {
        // Sample from MAL detailedmid=9 (Tsubasa: RESERVoir CHRoNiCLE manga)
        var html = """
            <div id="chaplayer">
            <div class="normal_header">Tsubasa: RESERVoir CHRoNiCLE Chapter Details</div>
            <div class="spaceit_pad" id="chaprow47371937">Chapter 233, read on 04/16/2013 at 09:21 <a href="#">Remove</a></div>
            <div class="spaceit_pad" id="chaprow46754709">Chapter 158, read on 04/04/2013 at 00:15 <a href="#">Remove</a></div>
            <div class="spaceit_pad" id="chaprow41872940">Chapter 82, read on 12/14/2012 at 23:43 <a href="#">Remove</a></div>
            <div class="spaceit_pad" id="chaprow38765363">Chapter 13, read on 09/29/2012 at 23:35 <a href="#">Remove</a></div>
            </div>
            """;

        var result = MalHistoryParser.Parse(html);

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalSessions);
        Assert.Equal(new DateTime(2012, 9, 29), result.LatestStartDate);
        Assert.Equal(new DateTime(2013, 4, 16), result.LatestEndDate);
    }
}
