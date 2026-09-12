using System;
using System.Buffers;

namespace Kiriha;

partial class Program
{
    private static readonly string[] SensitiveQueryKeys = ["code", "token", "access_token", "refresh_token", "client_secret"];
    private static readonly SearchValues<char> SensitiveArgSeparators = SearchValues.Create("& ?#");

    /// <summary>
    /// Masks OAuth-sensitive query parameters in command-line arguments so they don't
    /// leak into Serilog files. The OS sometimes routes OAuth callbacks via the
    /// command line (custom URI schemes), and full URLs would otherwise hit disk.
    /// </summary>
    private static string MaskSensitiveArgs(string[] args)
    {
        if (args is null || args.Length == 0) return string.Empty;
        var masked = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i] ?? string.Empty;
            foreach (var key in SensitiveQueryKeys)
            {
                var idx = a.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var endRel = a.AsSpan(idx).IndexOfAny(SensitiveArgSeparators);
                var end = endRel < 0 ? a.Length : idx + endRel;
                a = string.Concat(a.AsSpan(0, idx + key.Length + 1), "***", a.AsSpan(end));
            }
            masked[i] = a;
        }
        return string.Join(" ", masked);
    }
}
