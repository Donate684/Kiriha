using System;
using System.Linq;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
    public static bool IsPlayerMode(string[] args) =>
        args.Any(arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));

    private static string? GetArgValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
            return null;

        var value = args[index + 1];
        return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
    }

    private static string GetPlayerVideoUrl(string[] args)
    {
        var playerArgIndex = Array.FindIndex(args, arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));
        if (playerArgIndex >= 0 && playerArgIndex + 1 < args.Length && !args[playerArgIndex + 1].StartsWith("--"))
            return args[playerArgIndex + 1];

        return string.Empty;
    }
}
