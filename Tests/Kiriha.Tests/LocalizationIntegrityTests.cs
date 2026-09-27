using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Kiriha.Tests;

/// <summary>
/// Architectural and integrity tests ensuring all localization keys referenced in C# and XAML
/// actually exist in the i18n JSON files, that language files maintain parity, and that
/// raw unlocalized keys (like "auth.close_window") are not accidentally emitted to users.
/// </summary>
public class LocalizationIntegrityTests
{
    private static readonly string SrcRoot = ResolveSrcRoot();
    private static readonly string I18nRoot = Path.Combine(SrcRoot, "Kiriha", "Assets", "i18n");

    private static string ResolveSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "Kiriha.Tests")
            dir = dir.Parent;

        var root = dir?.Parent?.Parent;
        var src = root != null ? Path.Combine(root.FullName, "src") : null;
        Assert.True(src != null && Directory.Exists(src),
            $"Could not locate /src directory. Searched from: {AppContext.BaseDirectory}");
        return src!;
    }

    private static Dictionary<string, string> LoadFlattenedKeys(string langCode)
    {
        var dir = Path.Combine(I18nRoot, langCode);
        Assert.True(Directory.Exists(dir), $"i18n directory not found: {dir}");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var ns = Path.GetFileNameWithoutExtension(file);
            var json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            FlattenJson(doc.RootElement, ns, result);
        }

        return result;
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenJson(property.Value, string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}", result);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJson(item, $"{prefix}[{index}]", result);
                    index++;
                }
                break;
            default:
                result[prefix] = element.ToString();
                break;
        }
    }

    [Fact(DisplayName = "i18n files must maintain parity between English and Russian")]
    public void LanguageFiles_MustHaveParity()
    {
        var enKeys = LoadFlattenedKeys("en");
        var ruKeys = LoadFlattenedKeys("ru");

        var missingInRu = enKeys.Keys.Where(k => !ruKeys.ContainsKey(k)).ToList();
        var missingInEn = ruKeys.Keys.Where(k => !enKeys.ContainsKey(k)).ToList();

        var errors = new List<string>();
        if (missingInRu.Count > 0)
            errors.Add($"Missing in Russian ({missingInRu.Count}):\n  " + string.Join("\n  ", missingInRu));
        if (missingInEn.Count > 0)
            errors.Add($"Missing in English ({missingInEn.Count}):\n  " + string.Join("\n  ", missingInEn));

        Assert.True(errors.Count == 0, string.Join("\n\n", errors));
    }

    [Fact(DisplayName = "All literal keys passed to GetLoc(...) must exist in i18n dictionaries")]
    public void All_GetLoc_LiteralKeys_MustExistInLocalization()
    {
        var enKeys = LoadFlattenedKeys("en");
        var ruKeys = LoadFlattenedKeys("ru");

        var getLocRegex = new Regex(@"GetLoc\(\s*""([a-zA-Z0-9_\.]+)""", RegexOptions.Compiled);
        var violations = new List<string>();

        var csFiles = Directory.GetFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"));

        foreach (var file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;

                var matches = getLocRegex.Matches(line);
                foreach (Match m in matches)
                {
                    var key = m.Groups[1].Value;

                    // Skip dynamic prefix helpers like "filters.sort." or "anime.status."
                    if (key.EndsWith('.')) continue;

                    bool existsInEn = enKeys.ContainsKey(key);
                    bool existsInRu = ruKeys.ContainsKey(key);

                    if (!existsInEn || !existsInRu)
                    {
                        var rel = Path.GetRelativePath(SrcRoot, file);
                        violations.Add($"{rel}:{i + 1} -> \"{key}\" (in EN: {existsInEn}, in RU: {existsInRu})");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} GetLoc(...) call(s) referencing non-existent localization keys:\n" +
            string.Join("\n", violations));
    }

    [Fact(DisplayName = "All {DynamicResource l.xxx} bindings in XAML must exist in i18n dictionaries")]
    public void All_Xaml_DynamicResourceKeys_MustExistInLocalization()
    {
        var enKeys = LoadFlattenedKeys("en");
        var ruKeys = LoadFlattenedKeys("ru");

        var xamlRegex = new Regex(@"\{DynamicResource\s+l\.([a-zA-Z0-9_\.]+)\}", RegexOptions.Compiled);
        var violations = new List<string>();

        var axamlFiles = Directory.GetFiles(SrcRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"));

        foreach (var file in axamlFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var matches = xamlRegex.Matches(line);
                foreach (Match m in matches)
                {
                    var key = m.Groups[1].Value;

                    bool existsInEn = enKeys.ContainsKey(key);
                    bool existsInRu = ruKeys.ContainsKey(key);

                    if (!existsInEn || !existsInRu)
                    {
                        var rel = Path.GetRelativePath(SrcRoot, file);
                        violations.Add($"{rel}:{i + 1} -> \"l.{key}\" (in EN: {existsInEn}, in RU: {existsInRu})");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} XAML {{DynamicResource l.key}} binding(s) referencing non-existent localization keys:\n" +
            string.Join("\n", violations));
    }

    [Fact(DisplayName = "No raw unlocalized keys like 'auth.close_window' in string literals outside localization")]
    public void No_Raw_Localization_Keys_In_String_Literals()
    {
        var ruKeys = LoadFlattenedKeys("ru");

        // Specific notorious keys that must never appear as raw string literal assignments
        var bannedRawKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "auth.close_window",
            "auth.success",
            "player.status.loading_video"
        };

        var violations = new List<string>();
        var csFiles = Directory.GetFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"));

        var stringLiteralRegex = new Regex(@"""([^""]+)""", RegexOptions.Compiled);

        foreach (var file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;
                if (line.Contains("GetLoc(")) continue;

                var matches = stringLiteralRegex.Matches(line);
                foreach (Match m in matches)
                {
                    var val = m.Groups[1].Value;
                    if (bannedRawKeys.Contains(val))
                    {
                        var rel = Path.GetRelativePath(SrcRoot, file);
                        violations.Add($"{rel}:{i + 1} -> raw literal \"{val}\" without GetLoc lookup: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} raw localization key(s) in string literals without GetLoc:\n" +
            string.Join("\n", violations));
    }
}
