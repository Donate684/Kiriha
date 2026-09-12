using System;
using System.Diagnostics.CodeAnalysis;

namespace Kiriha.Core.Domain.Extensions;

public static class StringExtensions
{
    [return: NotNullIfNotNull(nameof(s))]
    public static string? UppercaseFirst(this string? s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length == 1) return char.ToUpperInvariant(s[0]).ToString();
        return string.Create(s.Length, s, static (span, src) =>
        {
            span[0] = char.ToUpperInvariant(src[0]);
            src.AsSpan(1).CopyTo(span[1..]);
        });
    }
}
