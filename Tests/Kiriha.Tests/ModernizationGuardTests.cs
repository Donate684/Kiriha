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
/// - Collection expressions [...] instead of 'new[] { ... }'
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
}
