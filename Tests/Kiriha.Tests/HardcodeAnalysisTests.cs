using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kiriha.Tests;

/// <summary>
/// Static analysis tests that scan source files for hardcoded values that should
/// be centralised in AppConstants or other constant-class files.
///
/// HOW TO ADD EXCEPTIONS:
///   - For URL exceptions (CS):  add the allowed source-relative path to <see cref="UrlAllowedFiles"/>.
///   - For duplicate const exceptions: add the value to <see cref="DuplicateConstValueAllowList"/>.
///   - For magic string exceptions: add the value to <see cref="MagicStringAllowList"/>.
///   - For magic number exceptions: add the value to <see cref="MagicTimeoutAllowList"/>.
///   - For XAML Text exceptions: add the value to <see cref="XamlHardcodedTextAllowList"/>.
///   - For XAML Color exceptions: add the hex to <see cref="XamlHardcodedColorAllowList"/>.
///   - For XAML URL exceptions: add the path to <see cref="XamlUrlAllowedFiles"/>.
/// </summary>
public class HardcodeAnalysisTests
{
    // ─── Paths ────────────────────────────────────────────────────────────────

    /// <summary>Root of /src — resolved relative to the test binary output folder.</summary>
    private static readonly string SrcRoot = ResolveSrcRoot();

    private static string ResolveSrcRoot()
    {
        // Walk up from the test DLL output until we find the "Kiriha.Tests" folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "Kiriha.Tests")
            dir = dir.Parent;

        // dir is now e:\kiriha\Tests\Kiriha.Tests  →  go up two levels then into src
        var root = dir?.Parent?.Parent;
        var src  = root != null ? Path.Combine(root.FullName, "src") : null;

        Assert.True(src != null && Directory.Exists(src),
            $"Could not locate /src directory. Searched from: {AppContext.BaseDirectory}");

        return src!;
    }

    /// <summary>
    /// Files (relative to SrcRoot, forward-slash) where inline http(s) URLs are acceptable
    /// because that IS the canonical declaration (constants files, generated ports, etc.)
    /// </summary>
    private static readonly HashSet<string> UrlAllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Legitimate constant definitions
        "Kiriha.Core.Domain/Constants/AppConstants.cs",
        "Kiriha.Core.Domain/Constants/ApiKeys.cs",

        // Third-party vendored code — licence header URLs only
        "Kiriha.Infrastructure/Utils/Parsing/AnitomySharp/Anitomy.cs",
        "Kiriha.Infrastructure/Utils/Parsing/AnitomySharp/Element.cs",
        "Kiriha.Infrastructure/Utils/Parsing/AnitomySharp/Keyword.cs",
        "Kiriha.Infrastructure/Utils/Parsing/AnitomySharp/Options.cs",
        "Kiriha.Infrastructure/Utils/Parsing/AnitomySharp/Parser.cs",
        "Kiriha.Infrastructure/Utils/Parsing/AnitomySharp/ParserHelper.cs",
        "Kiriha.Infrastructure/Utils/Parsing/AnitomySharp/ParserNumber.cs",

        // UI credits model — decorative display data, not API config
        "Kiriha/Models/AboutCredits.cs",
    };

    /// <summary>
    /// Const string VALUES (case-sensitive) that are intentionally duplicated across files.
    /// </summary>
    private static readonly HashSet<string> DuplicateConstValueAllowList = new(StringComparer.Ordinal)
    {
        // AnitomySharp regex fragment constants (third-party port)
        @"\A(?:",
        @")\z",
    };

    /// <summary>
    /// Literal string values that are acceptable in product code outside of Constants/ files.
    /// Add the FULL string content (without surrounding quotes).
    /// </summary>
    private static readonly HashSet<string> MagicStringAllowList = new(StringComparer.Ordinal)
    {
        // FFmpeg loudnorm filter — technical, not a user-visible config string
        "loudnorm=I=-16:TP=-1.5:LRA=11",

        // mpv command/property keys — these are the mpv API surface
        "seek",
        "volume",
        "speed",

        // OS-defined registry paths (not app config)
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\App Paths\Kiriha.exe",
        @"Software\Classes\Applications\Kiriha.exe",
        @"Software\Classes\SystemFileAssociations\video\OpenWithList\Kiriha.exe",
        @"Software\Clients\Media\Kiriha",
        @"Software\Clients\Media\Kiriha\Capabilities",
        @"Software\RegisteredApplications",

        // Localization keys
        "anime.status.completed",
        "anime.status.plan_to_watch",
        "scrobbler.status.paused",
        "analytics.history.episodes_format",

        // Exception / log messages
        "AnimeCardTemplate.Poster_DoubleTapped failed",
        "Rate limit queue exceeded.",

        // Mime types & formats
        "application/json",
        "dd.MM HH:mm",

        // External API magic strings (MAL statuses)
        "currently airing",
        "finished airing",

        // MPV property names
        "demuxer-max-back-bytes",
        "input-default-bindings",
        "screenshot-jpeg-quality",

        // Cache keys / method names
        "LoadSeasonalAnimeAsync",

        // SQLite commands
        "PRAGMA wal_checkpoint(PASSIVE);",
    };

    /// <summary>
    /// Inline numeric timeout/delay values (int) that are acceptable — e.g. UI animation timings.
    /// </summary>
    private static readonly HashSet<int> MagicTimeoutAllowList = new()
    {
        // UI stagger animations — pure visual timing, safe to leave inline
        45, 140, 30,
    };

    // ─── XAML allow-lists ─────────────────────────────────────────────────────

    /// <summary>
    /// Hardcoded <c>Text="..."</c> values in AXAML that are intentionally not localised.
    /// Typical candidates: brand names displayed as-is, icon glyph codes, console-font badges.
    /// </summary>
    private static readonly HashSet<string> XamlHardcodedTextAllowList = new(StringComparer.Ordinal)
    {
        // App name — displayed verbatim in the About window hero
        "Kiriha",

        // Service brand names shown in the Settings Accounts page
        // (proper nouns that are NOT localised by convention)
        "Shikimori",
        "shikimori.one",
        "shikimori.rip / .net / …",  // actual ellipsis character in the source file
        "ORIGINAL",
        "FORK",

        // Keyboard shortcut hint placeholders in player settings
        "Shift+S",

        // Unicode bullet / separator characters used as visual decorators
        "•",   // U+2022 bullet (AmbiguousMatchView separator)
        "·",   // U+00B7 middle dot (HistoryView separator, also &#183;)
    };

    /// <summary>
    /// Hex colour literals that are acceptable directly in AXAML because they are
    /// intentional semantic colours without a theme resource equivalent
    /// (e.g. signal-green for "connected", signal-red for error).
    /// </summary>
    private static readonly HashSet<string> XamlHardcodedColorAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        // Transparent / fully-transparent placeholders used in animation origins
        "#00000000",

        // Shadow overlay on anime cards — purely decorative, no theme token needed
        "#AA000000",

        // Shikimori connection-status signal colours (green = connected, orange = fork, red = logout)
        "#152ecc71",  // 8% green tint background
        "#2ecc71",    // green text / icon
        "#15ff9500",  // 8% orange tint background
        "#cc8400",    // orange text
        "#ff5555",    // red logout icon
        "#2a9c5b",    // darker connected green

        // Transparent / semi-transparent overlays on posters and images (must remain static for contrast)
        "#55000000", "#66000000", "#BB000000", "#EE000000", "#88000000", "#CC000000",
        "#BFFFFFFF", "#30FFFFFF", "#A0FFFFFF", "#D0FFFFFF", "#25FFFFFF", "#40FFFFFF",
        "#A8FFFFFF", "#20FFFFFF", "#16FFFFFF", "#CCFFFFFF", "#D8FFFFFF", "#80FFFFFF", 
        "#55FFFFFF", "#E6FFFFFF", "#70FFFFFF",
    };

    /// <summary>
    /// AXAML files (relative to SrcRoot, forward-slash) where inline http(s) URLs are
    /// acceptable — add paths here only for files that intentionally embed URLs (e.g. help links).
    /// </summary>
    private static readonly HashSet<string> XamlUrlAllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Empty — all Avalonia xmlns URIs are filtered by the test itself (xmlns attribute pattern).
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> AllSourceFiles()
    {
        return Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\"));
    }

    /// <summary>
    /// All AXAML files under /src, excluding generated obj/bin output and the
    /// Kiriha.Mpv.UI mirror project (intentional duplicate of Kiriha views).
    /// </summary>
    private static IEnumerable<string> AllXamlFiles()
    {
        return Directory.EnumerateFiles(SrcRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains(@"\obj\") &&
                !f.Contains(@"\bin\") &&
                !RelativePath(f).StartsWith("Kiriha.Mpv.UI/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsXamlCommentLine(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("<!--");
    }

    /// <summary>
    /// Source files to exclude from the cross-file duplicate literal check.
    /// EF Core migration snapshots and the Kiriha.Mpv.UI mirror project are
    /// intentionally repetitive by design — flagging them creates noise.
    /// </summary>
    private static IEnumerable<string> SourceFilesForDuplicateCheck()
    {
        return AllSourceFiles()
            .Where(f =>
                // EF migrations are auto-generated — duplicates are by design
                !RelativePath(f).Contains("Migrations/", StringComparison.OrdinalIgnoreCase) &&
                // Kiriha.Mpv.UI is a deliberate mirror of Kiriha views
                !RelativePath(f).StartsWith("Kiriha.Mpv.UI/", StringComparison.OrdinalIgnoreCase));
    }

    private static string RelativePath(string absolute) =>
        Path.GetRelativePath(SrcRoot, absolute).Replace('\\', '/');

    private static bool IsInAllowedUrlFile(string absolutePath) =>
        UrlAllowedFiles.Contains(RelativePath(absolutePath));

    private static bool IsInConstantsFile(string absolutePath)
    {
        var rel = RelativePath(absolutePath);
        return rel.Contains("Constants/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCommentLine(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*");
    }

    // ─── Test 1: Inline http(s) URLs outside Constants/ ──────────────────────

    [Fact(DisplayName = "No inline http(s) URLs outside of Constants files")]
    public void NoInlineUrlsOutsideConstantsFiles()
    {
        // Matches string literals containing http:// or https://
        var urlInString = new Regex(@"""[^""]*https?://[^""]*""", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in AllSourceFiles())
        {
            if (IsInAllowedUrlFile(file)) continue;
            if (IsInConstantsFile(file)) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsCommentLine(lines[i])) continue;
                if (urlInString.IsMatch(lines[i]))
                    violations.Add($"  {RelativePath(file)}:{i + 1}  →  {lines[i].Trim()}");
            }
        }

        AssertNoViolations(
            violations,
            "Inline http(s) URLs found outside Constants/ files.",
            "Move the URL to AppConstants (or a dedicated constants class) and reference it by name.");
    }

    // ─── Test 2: Duplicate const string values shadowing AppConstants ─────────

    [Fact(DisplayName = "No const string values that duplicate AppConstants entries")]
    public void NoConstStringValuesDuplicatingAppConstants()
    {
        var knownConstants = CollectConstantValues();

        var constDecl = new Regex(
            @"^\s*(private|internal|public|protected)?\s*(static\s+)?const\s+string\s+\w+\s*=\s*""(?<val>[^""]+)""\s*;",
            RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in AllSourceFiles())
        {
            if (IsInConstantsFile(file)) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var m = constDecl.Match(lines[i]);
                if (!m.Success) continue;

                var val = m.Groups["val"].Value;
                if (DuplicateConstValueAllowList.Contains(val)) continue;

                if (knownConstants.TryGetValue(val, out var declaredIn))
                    violations.Add(
                        $"  {RelativePath(file)}:{i + 1}  →  \"{val}\"" +
                        $"  (already in {declaredIn})");
            }
        }

        AssertNoViolations(
            violations,
            "Const string values that duplicate entries already in AppConstants.",
            "Replace the local const with AppConstants.<Member>.");
    }

    private static Dictionary<string, string> CollectConstantValues()
    {
        var rx = new Regex(@"const\s+string\s+\w+\s*=\s*""(?<val>[^""]+)""", RegexOptions.Compiled);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in AllSourceFiles())
        {
            if (!IsInConstantsFile(file)) continue;
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
                result.TryAdd(m.Groups["val"].Value, RelativePath(file));
        }

        return result;
    }

    // ─── Test 3: Same non-trivial string literal in 2+ files ─────────────────

    [Fact(DisplayName = "No identical non-trivial string literals appearing in 2+ distinct source files")]
    public void NoDuplicateNonTrivialStringLiteralsAcrossFiles()
    {
        // Only strings longer than 10 chars to skip short tokens like "play", "open", etc.
        var literal = new Regex(@"""(?<val>[^""\\]{11,})""", RegexOptions.Compiled);

        // value → list of "file:line" occurrences
        var occurrences = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var file in SourceFilesForDuplicateCheck())
        {
            if (IsInConstantsFile(file)) continue;
            if (IsInAllowedUrlFile(file)) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsCommentLine(lines[i])) continue;

                foreach (Match m in literal.Matches(lines[i]))
                {
                    var val = m.Groups["val"].Value;
                    if (MagicStringAllowList.Contains(val)) continue;
                    if (IsTrivialString(val)) continue;

                    var rel = RelativePath(file);
                    if (!occurrences.TryGetValue(val, out var list))
                        occurrences[val] = list = [];

                    // Record at most one occurrence per file
                    if (!list.Any(l => l.StartsWith(rel + ":")))
                        list.Add($"{rel}:{i + 1}");
                }
            }
        }

        var violations = new List<string>();
        foreach (var (val, locs) in occurrences.OrderBy(x => x.Key))
        {
            var distinctFiles = locs.Select(l => l.Split(':')[0]).Distinct().ToList();
            if (distinctFiles.Count < 2) continue;

            violations.Add($"  \"{val}\"  — in {distinctFiles.Count} files:");
            foreach (var loc in locs)
                violations.Add($"      {loc}");
        }

        AssertNoViolations(
            violations,
            "Identical string literals found in multiple non-constants files.",
            "Extract the shared value into AppConstants or a dedicated constants class.");
    }

    private static bool IsTrivialString(string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return true;
        if (val.All(c => char.IsDigit(c) || c == '.' || c == '-')) return true;
        // Interpolated string fragments (contain {…}) — these compose values at runtime
        // and are not standalone config strings worth centralising.
        if (val.Contains('{') || val.Contains('}')) return true;
        // Single-word identifiers without path separators — too common to flag
        if (!val.Contains(' ') && !val.Contains('/') && !val.Contains('\\') && val.Length < 22)
            return true;
        // C# expression fragments that leaked through (e.g. ", out var x) ? x.GetString() ?? ")
        if (val.Contains(" ? ") || val.StartsWith(", ") || val.StartsWith(" || "))
            return true;
        return false;
    }

    // ─── Test 4: Magic timeout / delay numbers ────────────────────────────────

    [Fact(DisplayName = "No hardcoded timeout or delay magic numbers without a named constant")]
    public void NoHardcodedTimeoutMagicNumbers()
    {
        var patterns = new[]
        {
            new Regex(@"Task\.Delay\((?<val>\d{4,})\)",         RegexOptions.Compiled),
            new Regex(@"Thread\.Sleep\((?<val>\d{3,})\)",       RegexOptions.Compiled),
            new Regex(@"\.FromMilliseconds\((?<val>\d{4,})\)",  RegexOptions.Compiled),
            new Regex(@"\.FromSeconds\((?<val>\d{2,})\)",       RegexOptions.Compiled),
            new Regex(@"\.FromMinutes\((?<val>\d+)\)",          RegexOptions.Compiled),
        };

        // Patterns that indicate the number is already bound to a name —
        // these are acceptable (e.g. "private static readonly TimeSpan X = TimeSpan.FromSeconds(30)").
        var namedAssignment = new Regex(
            @"\w+\s*(?:=\s*[^=]|=>)",
            RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in AllSourceFiles())
        {
            if (IsInConstantsFile(file)) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsCommentLine(lines[i])) continue;

                // Skip lines where the timeout is the RHS of a named binding
                if (namedAssignment.IsMatch(lines[i])) continue;

                foreach (var rx in patterns)
                foreach (Match m in rx.Matches(lines[i]))
                {
                    if (int.TryParse(m.Groups["val"].Value, out var num)
                        && !MagicTimeoutAllowList.Contains(num))
                        violations.Add($"  {RelativePath(file)}:{i + 1}  →  {lines[i].Trim()}");
                }
            }
        }

        AssertNoViolations(
            violations,
            "Hardcoded timeout/delay numbers found in non-constants files.",
            "Extract the value into a named constant, e.g.: private const int SyncRetryDelayMs = 1500;");
    }

    // ─── Test 5: Hardcoded Text="..." in AXAML ────────────────────────────────

    [Fact(DisplayName = "No hardcoded Text values in AXAML (use DynamicResource or Binding)")]
    public void NoHardcodedTextInXaml()
    {
        // Matches:  Text="some literal"  — value doesn't start with { (no binding/resource).
        // Does NOT match:  Text="{DynamicResource ...}"  or  Text="{Binding ...}"
        // Also skips: &#x…; glyph codes (Segoe/Fluent icon chars), PlaceholderText (intentional hints)
        //             &#NNN; HTML numeric entities (bullet chars, separators, etc.)
        var hardcodedText = new Regex(
            @"(?<!Placeholder)Text=""(?!\{)(?!&?#)(?<val>[^""]{3,}|[^""]*[•—·«»][^""]*)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var violations = new List<string>();

        foreach (var file in AllXamlFiles())
        {
            // Read as UTF-8 so Cyrillic and Unicode chars match allow-list entries correctly
            var lines = File.ReadAllLines(file, System.Text.Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsXamlCommentLine(lines[i])) continue;

                foreach (Match m in hardcodedText.Matches(lines[i]))
                {
                    var val = m.Groups["val"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    // Skip pure-numeric strings (sizes, counts, etc.)
                    if (val.All(c => char.IsDigit(c) || c == '.' || c == '-' || c == ',')) continue;
                    if (XamlHardcodedTextAllowList.Contains(val)) continue;

                    violations.Add($"  {RelativePath(file)}:{i + 1}  \u2192  {lines[i].Trim()}");
                }
            }
        }

        AssertNoViolations(
            violations,
            "Hardcoded Text values found in AXAML files.",
            "Use {DynamicResource l.key} for localisable strings, or add the value to XamlHardcodedTextAllowList if it is a proper noun / intentional.");
    }

    // ─── Test 6: Hardcoded #RRGGBB colours in AXAML ───────────────────────────

    [Fact(DisplayName = "No hardcoded hex colours in AXAML (use DynamicResource theme tokens)")]
    public void NoHardcodedColorsInXaml()
    {
        // Matches hex colours in attribute values:  Background="#FF0000"  Color="#80AABBCC"
        // Skips theme/colour definition files and <Color x:Key="..."> resource declarations.
        var hexColor = new Regex(
            @"""(?<color>#[0-9A-Fa-f]{6,8})""",
            RegexOptions.Compiled);

        // Style/theme files are the canonical definition place — skip them
        var themeFilePatterns = new[] { "Themes.axaml", "Colors.axaml", "Styles.axaml", "Controls.axaml" };
        
        // Player overlay elements are intentionally hardcoded with fixed dark-theme
        // colors (semi-transparent whites/blacks) because they overlay video content.
        var playerPaths = new[] { "Views/Player/", "Views/Controls/Variants/NowPlayingCompact" };

        var violations = new List<string>();

        foreach (var file in AllXamlFiles())
        {
            var rel = RelativePath(file);
            if (themeFilePatterns.Any(p => rel.EndsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            if (playerPaths.Any(p => rel.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsXamlCommentLine(lines[i])) continue;
                // Skip <Color x:Key="..."> and <SolidColorBrush x:Key="..."> lines — those ARE the resource definitions
                var trimLine = lines[i].TrimStart();
                if (trimLine.StartsWith("<Color ") || trimLine.StartsWith("<SolidColorBrush ") || trimLine.StartsWith("<GradientStop ")) continue;

                foreach (Match m in hexColor.Matches(lines[i]))
                {
                    var color = m.Groups["color"].Value;
                    if (XamlHardcodedColorAllowList.Contains(color)) continue;
                    violations.Add($"  {rel}:{i + 1}  \u2192  {lines[i].Trim()}");
                }
            }
        }

        AssertNoViolations(
            violations,
            "Hardcoded hex colour literals found in AXAML files.",
            "Use a DynamicResource theme token instead, or add to XamlHardcodedColorAllowList with a comment explaining why it is a one-off signal colour.");
    }

    // ─── Test 7: Hardcoded http(s) URLs in AXAML ─────────────────────────────

    [Fact(DisplayName = "No hardcoded http(s) URLs in AXAML (xmlns declarations excluded)")]
    public void NoHardcodedUrlsInXaml()
    {
        // Matches a URL that is NOT the value of an xmlns attribute.
        // Strategy: find every "http(s)://..." in the line, then reject it if
        // the surrounding context is an xmlns="..." or xmlns:prefix="..." declaration.
        var urlPattern   = new Regex(@"""https?://(?<url>[^""]+)""", RegexOptions.Compiled);
        var xmlnsPattern = new Regex(@"xmlns(?::\w+)?=""https?://[^""]*""", RegexOptions.Compiled);

        // URLs that are intentional in XAML (example/placeholder values shown to the user)
        var placeholderUrlPatterns = new[]
        {
            "example.com",  // generic placeholder in custom-links page
            "rutracker.org",
            "nnmclub.to",
        };

        var violations = new List<string>();

        foreach (var file in AllXamlFiles())
        {
            var rel = RelativePath(file);
            if (XamlUrlAllowedFiles.Contains(rel)) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsXamlCommentLine(lines[i])) continue;

                foreach (Match m in urlPattern.Matches(lines[i]))
                {
                    var url = m.Groups["url"].Value;

                    // Skip xmlns namespace URIs — not runtime URLs
                    if (xmlnsPattern.IsMatch(lines[i])) continue;

                    // Skip intentional example/placeholder URLs shown to the user
                    if (placeholderUrlPatterns.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

                    violations.Add($"  {rel}:{i + 1}  \u2192  {lines[i].Trim()}");
                }
            }
        }

        AssertNoViolations(
            violations,
            "Hardcoded http(s) URLs found in AXAML files.",
            "Move the URL to AppConstants and expose it via a ViewModel property or DynamicResource.");
    }

    // ─── Test 8: Mojibake (Encoding artifacts) ────────────────────────────────

    [Fact(DisplayName = "No encoding artifacts (mojibake) in source files")]
    public void NoMojibakeInSourceFiles()
    {
        // When UTF-8 text (like Cyrillic or special punctuation) is read or saved using Windows-1251
        // or Latin-1, it produces predictable artifacts (mojibake / кракозябры):
        // - Cyrillic UTF-8 bytes (D0 xx, D1 xx) become 'Ð' (\u00D0) and 'Ñ' (\u00D1).
        // - UTF-8 punctuation (E2 80 xx) becomes 'â€' (\u00E2 \u20AC).
        // These characters are practically never used legitimately in this codebase.
        var mojibakePattern = new Regex(@"[\u00D0\u00D1]|\u00E2\u20AC", RegexOptions.Compiled);

        var violations = new List<string>();

        // Check both C# and XAML files
        var allFiles = AllSourceFiles().Concat(AllXamlFiles());

        foreach (var file in allFiles)
        {
            var rel = RelativePath(file);
            
            // Read raw text. We use UTF-8. If the file contains these literal characters,
            // it means they are actually saved in the file as these characters (which happens
            // when a file is corrupted by double-encoding or wrong-encoding saves).
            var lines = File.ReadAllLines(file, System.Text.Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                if (mojibakePattern.IsMatch(lines[i]))
                {
                    violations.Add($"  {rel}:{i + 1}  \u2192  {lines[i].Trim()}");
                }
            }
        }

        AssertNoViolations(
                violations,
            "Encoding artifacts (mojibake / кракозябры) found in source files.",
            "Fix the corrupted characters. They were likely caused by saving a UTF-8 file in a different encoding, or pasting wrongly-encoded text.");
    }

    // ─── Test 9: No Cyrillic strings in C# ────────────────────────────────────

    [Fact(DisplayName = "No hardcoded Cyrillic strings in C# (use ILocalizer)")]
    public void NoCyrillicStringsInCSharp()
    {
        // Finds any Cyrillic characters inside double quotes (string literals) or interpolated strings.
        // It skips comments by ignoring lines that start with // (after optional whitespace).
        // Matches: "какой-то текст", $"значение: {val} рублей"
        // Does NOT match: // Это комментарий по-русски
        var cyrillicStringPattern = new Regex(@"^(?!\s*\/\/).*""[^""]*[А-Яа-яЁё][^""]*""", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in AllSourceFiles())
        {
            var rel = RelativePath(file);
            // AppConstants often contains default English/Russian names that are conceptually valid constants.
            if (rel.EndsWith("AppConstants.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var lines = File.ReadAllLines(file, System.Text.Encoding.UTF8);

            for (int i = 0; i < lines.Length; i++)
            {
                if (cyrillicStringPattern.IsMatch(lines[i]))
                {
                    violations.Add($"  {rel}:{i + 1}  \u2192  {lines[i].Trim()}");
                }
            }
        }

        AssertNoViolations(
            violations,
            "Hardcoded Cyrillic string literals found in C# source files.",
            "Move the text to the localization files (i18n/*.json) and use ILocalizer.GetLoc().");
    }

    // ─── Assertion helper ─────────────────────────────────────────────────────

    private static void AssertNoViolations(List<string> violations, string headline, string hint)
    {
        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"HARDCODE VIOLATION: {headline}");
        sb.AppendLine($"Hint: {hint}");
        sb.AppendLine($"Violations ({violations.Count}):");
        foreach (var v in violations)
            sb.AppendLine(v);

        Assert.Fail(sb.ToString());
    }
}
