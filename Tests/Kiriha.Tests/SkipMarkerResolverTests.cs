using Kiriha.Core.Domain.Models;
using Kiriha.Mpv;
using Kiriha.Mpv.UI.Services.Player;
using Xunit;

namespace Kiriha.Tests;

public class SkipMarkerResolverTests
{
    [Theory]
    [InlineData("OP", SkipSegmentType.Intro)]
    [InlineData("op", SkipSegmentType.Intro)]
    [InlineData("OP1", SkipSegmentType.Intro)]
    [InlineData("OP 1", SkipSegmentType.Intro)]
    [InlineData("Opening", SkipSegmentType.Intro)]
    [InlineData("Opening Theme", SkipSegmentType.Intro)]
    [InlineData("Intro", SkipSegmentType.Intro)]
    [InlineData("NCOP", SkipSegmentType.Intro)]
    [InlineData("Опенинг", SkipSegmentType.Intro)]
    [InlineData("ED", SkipSegmentType.Outro)]
    [InlineData("ed", SkipSegmentType.Outro)]
    [InlineData("ED 1", SkipSegmentType.Outro)]
    [InlineData("ED1", SkipSegmentType.Outro)]
    [InlineData("Ending", SkipSegmentType.Outro)]
    [InlineData("Ending Theme", SkipSegmentType.Outro)]
    [InlineData("Outro", SkipSegmentType.Outro)]
    [InlineData("NCED", SkipSegmentType.Outro)]
    [InlineData("Credits", SkipSegmentType.Outro)]
    [InlineData("Эндинг", SkipSegmentType.Outro)]
    [InlineData("Титры", SkipSegmentType.Outro)]
    [InlineData("Recap", SkipSegmentType.Recap)]
    [InlineData("Рекап", SkipSegmentType.Recap)]
    [InlineData("Preview", SkipSegmentType.Preview)]
    [InlineData("Превью", SkipSegmentType.Preview)]
    [InlineData("Mixed OP", SkipSegmentType.MixedIntro)]
    [InlineData("mixed-op", SkipSegmentType.MixedIntro)]
    [InlineData("Mixed Opening", SkipSegmentType.MixedIntro)]
    [InlineData("Смешанный опенинг", SkipSegmentType.MixedIntro)]
    [InlineData("Mixed ED", SkipSegmentType.MixedOutro)]
    [InlineData("mixed-ed", SkipSegmentType.MixedOutro)]
    [InlineData("Mixed Ending", SkipSegmentType.MixedOutro)]
    [InlineData("Смешанный эндинг", SkipSegmentType.MixedOutro)]
    public void ClassifyChapterTitle_IdentifiesCorrectSkipType(string title, SkipSegmentType expected)
    {
        var result = SkipMarkerResolver.ClassifyChapterTitle(title);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Episode 01")]
    [InlineData("Part A")]
    [InlineData("Part B")]
    [InlineData("Main")]
    [InlineData("Scene 1")]
    [InlineData("Operation Red")]
    [InlineData("Special")]
    [InlineData("")]
    [InlineData(null)]
    public void ClassifyChapterTitle_ReturnsNullForRegularChapters(string? title)
    {
        var result = SkipMarkerResolver.ClassifyChapterTitle(title);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveChapters_CreatesValidSegmentsWithCalculatedEnds()
    {
        var chapters = new List<ChapterInfo>
        {
            new() { Title = "Intro / Recap", Time = 0.0 },
            new() { Title = "Opening", Time = 60.0 },
            new() { Title = "Part A", Time = 150.0 },
            new() { Title = "Part B", Time = 700.0 },
            new() { Title = "Ending", Time = 1350.0 },
            new() { Title = "Preview", Time = 1440.0 }
        };

        double duration = 1470.0;
        var segments = SkipMarkerResolver.ResolveChapters(chapters, duration);

        Assert.Equal(4, segments.Count);

        // Recap
        Assert.Equal(SkipSegmentType.Recap, segments[0].Type);
        Assert.Equal(0.0, segments[0].StartSeconds);
        Assert.Equal(60.0, segments[0].EndSeconds);

        // Opening
        Assert.Equal(SkipSegmentType.Intro, segments[1].Type);
        Assert.Equal(60.0, segments[1].StartSeconds);
        Assert.Equal(150.0, segments[1].EndSeconds);

        // Ending
        Assert.Equal(SkipSegmentType.Outro, segments[2].Type);
        Assert.Equal(1350.0, segments[2].StartSeconds);
        Assert.Equal(1440.0, segments[2].EndSeconds);

        // Preview
        Assert.Equal(SkipSegmentType.Preview, segments[3].Type);
        Assert.Equal(1440.0, segments[3].StartSeconds);
        Assert.Equal(1470.0, segments[3].EndSeconds);
    }

    [Fact]
    public void MergeSegments_AniSkipPrecedenceOverChapters()
    {
        var aniSkip = new List<SkipSegment>
        {
            new(SkipSegmentType.Intro, 65.5, 155.5, "Opening", "aniskip")
        };

        var chapters = new List<SkipSegment>
        {
            new(SkipSegmentType.Intro, 60.0, 150.0, "OP", "chapter"),
            new(SkipSegmentType.Outro, 1350.0, 1440.0, "ED", "chapter")
        };

        var merged = SkipMarkerResolver.MergeSegments(aniSkip, chapters, 1470.0);

        Assert.Equal(2, merged.Count);
        // AniSkip segment wins for Intro
        Assert.Equal("aniskip", merged[0].Source);
        Assert.Equal(65.5, merged[0].StartSeconds);
        Assert.Equal(155.5, merged[0].EndSeconds);

        // Chapter segment preserved for Outro
        Assert.Equal("chapter", merged[1].Source);
        Assert.Equal(1350.0, merged[1].StartSeconds);
        Assert.Equal(1440.0, merged[1].EndSeconds);
    }

    [Fact]
    public void GetSeekTarget_GuardsAgainstEOF()
    {
        double duration = 1440.0;
        var endingSegment = new SkipSegment(SkipSegmentType.Outro, 1350.0, 1440.0, "ED", "chapter");

        var target = SkipMarkerResolver.GetSeekTarget(endingSegment, duration);

        // Must not seek to exact EOF 1440.0, should seek to 1440.0 - 0.25 = 1439.75
        Assert.True(target < duration);
        Assert.Equal(1439.75, target, precision: 2);
    }

    [Fact]
    public void SkipOpeningEndingMode_DefaultsToPrompt()
    {
        var config = new AppSettings.PlayerConfig();
        Assert.Equal(SkipOpeningEndingMode.Prompt, config.SkipOpeningEnding);
    }
}
