using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kiriha.Tests;

/// <summary>
/// Regression guard tests that enforce modern .NET 10 / C# 14 coding conventions:
/// - System.Threading.Lock instead of legacy 'readonly object' lock fields
/// - ArgumentNullException.ThrowIfNull instead of 'throw new ArgumentNullException'
/// - Collection expressions [...] instead of 'new[] { ... }', Array.Empty&lt;T&gt;(), Enumerable.Empty&lt;T&gt;()
/// - Value tuples (T1, T2) instead of legacy System.Tuple class
/// - FrozenDictionary / FrozenSet for immutable static lookup collections
/// </summary>
public class ModernizationGuardTests
{
    private static readonly string SrcRoot = ResolveSrcRoot();

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

    [Fact]
    public void NoLegacyObjectLockFields_ShouldBeZero()
    {
        var files = Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories);
        var legacyLockRegex = new Regex(@"\breadonly\s+object\s+_\w+", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(SrcRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                if (legacyLockRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{relativePath}:{i + 1} -> {lines[i].Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} legacy 'readonly object' lock fields that should use System.Threading.Lock:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void NoLegacyArgumentNullExceptionThrows_ShouldBeZero()
    {
        var files = Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories);
        var throwNullRegex = new Regex(@"throw\s+new\s+ArgumentNullException\b", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(SrcRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                if (throwNullRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{relativePath}:{i + 1} -> {lines[i].Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} legacy 'throw new ArgumentNullException' that should use ArgumentNullException.ThrowIfNull:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void NoLegacyNewArrayInitializers_ShouldBeZeroOutsideMigrations()
    {
        var files = Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories);
        var newArrayRegex = new Regex(@"\bnew\s*\[\s*\]\s*\{", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(SrcRoot, file).Replace('\\', '/');

            // Skip auto-generated EF Core migrations
            if (relativePath.StartsWith("Kiriha.Data/Migrations/", StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                if (newArrayRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{relativePath}:{i + 1} -> {lines[i].Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} legacy 'new[] {{' that should use collection expressions [...]:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void NoLegacyTupleClass_ShouldBeZero()
    {
        var files = Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories);
        var tupleRegex = new Regex(@"\bTuple(\.Create)?<", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(SrcRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*"))
                    continue;

                if (tupleRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{relativePath}:{i + 1} -> {trimmed}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} legacy 'Tuple<...>' usages that should use C# value tuples (T1, T2):\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void NoArrayEmpty_ShouldBeZero()
    {
        var files = Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories);
        var arrayEmptyRegex = new Regex(@"\bArray\.Empty<", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(SrcRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*"))
                    continue;

                if (arrayEmptyRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{relativePath}:{i + 1} -> {trimmed}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} legacy 'Array.Empty<T>()' calls that should use collection expression '[]':\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void NoEnumerableEmpty_ShouldBeZero()
    {
        var files = Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories);
        var enumerableEmptyRegex = new Regex(@"\bEnumerable\.Empty<", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(SrcRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*"))
                    continue;

                if (enumerableEmptyRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{relativePath}:{i + 1} -> {trimmed}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} legacy 'Enumerable.Empty<T>()' calls that should use collection expression '[]':\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void NoLegacyStaticDictionaryOrHashSet_ShouldBeZero()
    {
        var files = Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories);
        var legacyStaticCollectionRegex = new Regex(@"\bstatic\s+readonly\s+(Dictionary|HashSet)<", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(SrcRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*"))
                    continue;

                if (legacyStaticCollectionRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{relativePath}:{i + 1} -> {trimmed}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} 'static readonly (Dictionary|HashSet)' fields that should use FrozenDictionary / FrozenSet:\n" +
            string.Join("\n", violations));
    }
}
