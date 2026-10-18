using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.ViewModels.Player;
using Xunit;

namespace Kiriha.Tests;

public class PlayerOverlaySkipPromptTests
{
    [Fact]
    public void ShowSkipPrompt_SetsVisibilityOpacityAndHitTest()
    {
        using var overlay = new PlayerOverlayViewModel();
        var segment = new SkipSegment(SkipSegmentType.Intro, 10, 100, "Opening", "aniskip");

        overlay.ShowSkipPrompt(segment, "Пропустить опенинг", "до 01:40");

        Assert.True(overlay.IsSkipPromptVisible);
        Assert.True(overlay.IsSkipPromptHitTestVisible);
        Assert.Equal(1.0, overlay.SkipPromptOpacity);
        Assert.Equal("Пропустить опенинг", overlay.SkipPromptTitle);
        Assert.Equal("до 01:40", overlay.SkipPromptSubtitle);
        Assert.Same(segment, overlay.ActiveSkipSegment);
    }

    [Fact]
    public void DismissSkipPrompt_Smooth_FadesOutAndDisablesHitTestImmediately()
    {
        using var overlay = new PlayerOverlayViewModel();
        var segment = new SkipSegment(SkipSegmentType.Intro, 10, 100, "Opening", "aniskip");
        overlay.ShowSkipPrompt(segment, "Пропустить опенинг", "до 01:40");

        SkipSegment? dismissedSegment = null;
        overlay.SkipPromptDismissed += seg => dismissedSegment = seg;

        overlay.DismissSkipPrompt(smooth: true);

        // Opacity should be 0 and hit testing disabled immediately to prevent click interception during fade
        Assert.Equal(0.0, overlay.SkipPromptOpacity);
        Assert.False(overlay.IsSkipPromptHitTestVisible);
        // IsSkipPromptVisible and content stay alive during the fade animation
        Assert.True(overlay.IsSkipPromptVisible);
        Assert.Same(segment, overlay.ActiveSkipSegment);
        Assert.Same(segment, dismissedSegment);
    }

    [Fact]
    public void DismissSkipPrompt_Immediate_CleansUpStateWithoutWaitingForFade()
    {
        using var overlay = new PlayerOverlayViewModel();
        var segment = new SkipSegment(SkipSegmentType.Intro, 10, 100, "Opening", "aniskip");
        overlay.ShowSkipPrompt(segment, "Пропустить опенинг", "до 01:40");

        SkipSegment? dismissedSegment = null;
        overlay.SkipPromptDismissed += seg => dismissedSegment = seg;

        overlay.DismissSkipPrompt(smooth: false);

        Assert.False(overlay.IsSkipPromptVisible);
        Assert.False(overlay.IsSkipPromptHitTestVisible);
        Assert.Equal(0.0, overlay.SkipPromptOpacity);
        Assert.Null(overlay.ActiveSkipSegment);
        Assert.Empty(overlay.SkipPromptTitle);
        Assert.Same(segment, dismissedSegment);
    }

    [Fact]
    public void PauseAndResumeSkipPromptTimer_DoesNotThrow()
    {
        using var overlay = new PlayerOverlayViewModel();
        var segment = new SkipSegment(SkipSegmentType.Intro, 10, 100, "Opening", "aniskip");
        overlay.ShowSkipPrompt(segment, "Пропустить опенинг", "до 01:40");

        overlay.PauseSkipPromptTimer();
        Assert.True(overlay.IsSkipPromptVisible);

        overlay.ResumeSkipPromptTimer();
        Assert.True(overlay.IsSkipPromptVisible);
    }
}
