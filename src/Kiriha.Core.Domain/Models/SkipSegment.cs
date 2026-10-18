namespace Kiriha.Core.Domain.Models;

public enum SkipOpeningEndingMode
{
    Disabled = 0,
    Prompt = 1,
    Auto = 2
}

public enum SkipSegmentType
{
    Intro,
    Outro,
    MixedIntro,
    MixedOutro,
    Recap,
    Preview
}

public sealed record SkipSegment(
    SkipSegmentType Type,
    double StartSeconds,
    double EndSeconds,
    string Title,
    string Source)
{
    public bool IsValid => EndSeconds > StartSeconds && StartSeconds >= 0;
}

public sealed record SkipOpeningEndingOption(string Name, SkipOpeningEndingMode Value);

public static class SkipOpeningEndingOptionFactory
{
    public static List<SkipOpeningEndingOption> CreateDefaultOptions(Func<string, string?>? getLoc) =>
    [
        new(getLoc?.Invoke("player.skip.mode_disabled") ?? "Disabled", SkipOpeningEndingMode.Disabled),
        new(getLoc?.Invoke("player.skip.mode_prompt") ?? "Prompt to skip", SkipOpeningEndingMode.Prompt),
        new(getLoc?.Invoke("player.skip.mode_auto") ?? "Automatically", SkipOpeningEndingMode.Auto)
    ];
}
