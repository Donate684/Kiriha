using System.Text;
using System.Text.RegularExpressions;

namespace Kiriha.ViewModels.Torrents;

/// <summary>
/// Calculates semantic and textual relevance between online anime search candidates and target search queries.
/// </summary>
public static class OnlineRelevanceScorer
{
    /// <summary>
    /// Computes the maximum relevance score among multiple target queries (e.g. user input, Russian title, Romaji title).
    /// </summary>
    public static int ComputeMaxRelevance(IEnumerable<string> targets, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return 0;

        var maxScore = 0;
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target)) continue;
            var score = Score(target, candidate);
            if (score > maxScore)
            {
                maxScore = score;
                if (maxScore >= 10000)
                    break;
            }
        }

        return maxScore;
    }

    /// <summary>
    /// Scores a single candidate title against a single target query.
    /// Higher values indicate higher relevance (exact matches score 10000).
    /// </summary>
    public static int Score(string query, string candidate)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(candidate))
            return 0;

        var q = Normalize(query);
        var c = Normalize(candidate);

        if (string.IsNullOrEmpty(q) || string.IsNullOrEmpty(c))
            return 0;

        // 1. Exact normalized match (ignoring case, punctuation, whitespace differences)
        if (string.Equals(q, c, StringComparison.Ordinal))
            return 10000;

        // 2. Candidate starts with query (e.g. "Title: Subtitle" when user searched "Title")
        if (c.StartsWith(q + " ", StringComparison.Ordinal) || c.StartsWith(q + ":", StringComparison.Ordinal))
            return 8500 - Math.Min(500, (c.Length - q.Length) * 5);

        // 3. Query starts with candidate
        if (q.StartsWith(c + " ", StringComparison.Ordinal))
            return 8000;

        // 4. Candidate contains full query phrase
        var phraseIndex = c.IndexOf(q, StringComparison.Ordinal);
        if (phraseIndex >= 0)
        {
            var posBonus = Math.Max(0, 200 - phraseIndex * 10);
            var lenPenalty = Math.Min(500, (c.Length - q.Length) * 5);
            return 7000 + posBonus - lenPenalty;
        }

        // 5. Query contains full candidate phrase
        if (q.Contains(c, StringComparison.Ordinal))
            return 6000;

        // 6. Word-level analysis
        var qWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cWords = c.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (qWords.Length == 0 || cWords.Length == 0)
            return 0;

        var matchedWordsCount = 0;
        var partialWordsCount = 0;

        foreach (var qw in qWords)
        {
            var foundExact = false;
            var foundPrefix = false;

            foreach (var cw in cWords)
            {
                if (string.Equals(qw, cw, StringComparison.Ordinal))
                {
                    foundExact = true;
                    break;
                }
                if (qw.Length >= 3 && cw.StartsWith(qw, StringComparison.Ordinal))
                {
                    foundPrefix = true;
                }
            }

            if (foundExact) matchedWordsCount++;
            else if (foundPrefix) partialWordsCount++;
        }

        if (matchedWordsCount == 0 && partialWordsCount == 0)
            return 0;

        double coverage = (matchedWordsCount + (partialWordsCount * 0.5)) / qWords.Length;
        var allMatchedBonus = (matchedWordsCount == qWords.Length) ? 2000 : 0;
        double density = (double)matchedWordsCount / cWords.Length;

        return (int)(coverage * 3000 + density * 1000 + allMatchedBonus);
    }

    /// <summary>
    /// Normalizes a string by converting to lowercase, replacing punctuation with spaces, and collapsing whitespace.
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var span = input.AsSpan().Trim();
        var sb = new StringBuilder(span.Length);
        foreach (var ch in span)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(' ');
            }
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
