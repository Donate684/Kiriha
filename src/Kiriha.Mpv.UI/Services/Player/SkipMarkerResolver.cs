using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv;

namespace Kiriha.Mpv.UI.Services.Player;

public static partial class SkipMarkerResolver
{
    private const double EofSeekGuardSeconds = 0.25;

    // Mixed Intro: mixed op, mixed intro, mixed opening, mixed-op, смешанный опенинг
    [GeneratedRegex(@"(^|[^\p{L}\p{N}])(mixed[-_ ]*(op\d*|ncop\d*|intro|opening(\s+theme)?)|(?:mixed|\u0441\u043c\u0435\u0448\u0430\u043d\u043d\w*)[-_ ]*(?:\u043e\u043f\u0435\u043d\u0438\u043d\u0433|\u0437\u0430\u0441\u0442\u0430\u0432\u043a\u0430))($|[^\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MixedIntroRegex();

    // Mixed Outro: mixed ed, mixed outro, mixed ending, mixed-ed, смешанный эндинг
    [GeneratedRegex(@"(^|[^\p{L}\p{N}])(mixed[-_ ]*(ed\d*|nced\d*|outro|ending(\s+theme)?|credits?)|(?:mixed|\u0441\u043c\u0435\u0448\u0430\u043d\u043d\w*)[-_ ]*(?:\u044d\u043d\u0434\u0438\u043d\u0433|\u0442\u0438\u0442\u0440\u044b))($|[^\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MixedOutroRegex();

    // Intro: op, op1, ncop, intro, opening, opening theme, опенинг, заставка
    [GeneratedRegex(@"(^|[^\p{L}\p{N}])(op\d*|ncop\d*|intro|opening(\s+theme)?|theme(\s+song)?|\u043e\u043f\u0435\u043d\u0438\u043d\u0433|\u0437\u0430\u0441\u0442\u0430\u0432\u043a\u0430)($|[^\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntroRegex();

    // Outro: ed, ed1, nced, outro, ending, ending theme, credits, credit, эндинг, титры
    [GeneratedRegex(@"(^|[^\p{L}\p{N}])(ed\d*|nced\d*|outro|ending(\s+theme)?|credits?|\u044d\u043d\u0434\u0438\u043d\u0433|\u0442\u0438\u0442\u0440\u044b)($|[^\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OutroRegex();

    // Recap: recap, рекап, summary
    [GeneratedRegex(@"(^|[^\p{L}\p{N}])(recap|\u0440\u0435\u043a\u0430\u043f|summary)($|[^\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RecapRegex();

    // Preview: preview, превью, анонс
    [GeneratedRegex(@"(^|[^\p{L}\p{N}])(preview|\u043f\u0440\u0435\u0432\u044c\u044e|\u0430\u043d\u043e\u043d\u0441)($|[^\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreviewRegex();

    public static SkipSegmentType? ClassifyChapterTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var clean = title.Trim();

        if (RecapRegex().IsMatch(clean))
            return SkipSegmentType.Recap;

        if (PreviewRegex().IsMatch(clean))
            return SkipSegmentType.Preview;

        if (MixedIntroRegex().IsMatch(clean))
            return SkipSegmentType.MixedIntro;

        if (MixedOutroRegex().IsMatch(clean))
            return SkipSegmentType.MixedOutro;

        bool hasOutro = OutroRegex().IsMatch(clean);
        bool hasIntro = IntroRegex().IsMatch(clean);

        // If title contains explicit outro marker (ending, ed, credits), it's outro (e.g. 'Ending Theme')
        if (hasOutro && (!hasIntro || clean.Contains("ending", StringComparison.OrdinalIgnoreCase) || clean.Contains("ed", StringComparison.OrdinalIgnoreCase)))
            return SkipSegmentType.Outro;

        if (hasIntro)
            return SkipSegmentType.Intro;

        if (hasOutro)
            return SkipSegmentType.Outro;

        return null;
    }

    public static List<SkipSegment> ResolveChapters(IReadOnlyList<ChapterInfo> chapters, double durationSeconds)
    {
        if (chapters == null || chapters.Count == 0 || !double.IsFinite(durationSeconds) || durationSeconds <= 0)
            return new List<SkipSegment>();

        var validChapters = chapters
            .Where(c => double.IsFinite(c.Time) && c.Time >= 0 && c.Time < durationSeconds)
            .OrderBy(c => c.Time)
            .ToList();

        var result = new List<SkipSegment>();

        for (int i = 0; i < validChapters.Count; i++)
        {
            var current = validChapters[i];
            var type = ClassifyChapterTitle(current.Title);
            if (!type.HasValue)
                continue;

            double nextStart = durationSeconds;
            for (int j = i + 1; j < validChapters.Count; j++)
            {
                if (validChapters[j].Time > current.Time)
                {
                    nextStart = validChapters[j].Time;
                    break;
                }
            }

            var end = Math.Min(nextStart, durationSeconds);
            if (end > current.Time)
            {
                result.Add(new SkipSegment(
                    type.Value,
                    current.Time,
                    end,
                    current.Title,
                    "chapter"));
            }
        }

        return result;
    }


    public static double GetSeekTarget(SkipSegment segment, double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            return segment.EndSeconds;

        var end = Math.Min(segment.EndSeconds, durationSeconds);
        if (end < durationSeconds)
            return end;

        var guarded = durationSeconds - EofSeekGuardSeconds;
        return guarded > segment.StartSeconds ? guarded : segment.StartSeconds;
    }
}
