using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Infrastructure.Player;

public static class PlayerProcessBridge
{
    public const string MutexName = "Kiriha.PlayerInstance";
    public const string PipeName = "Kiriha.PlayerInstance";
    public const string ResidentArg = "--player-resident";
    public const string ShutdownArg = "--shutdown-player";
    public const string UpdateMetadataArg = "--player-update-metadata";

    public static bool IsResident(string[] args) =>
        Array.Exists(args, arg => arg.Equals(ResidentArg, StringComparison.OrdinalIgnoreCase));

    public static bool TryForward(string[] args, int timeoutMs = 300)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(PipeArgumentSerializer.Serialize(args));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void StartResident()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)) return;

        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var isDotnet = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = processPath,
            Arguments = isDotnet && !string.IsNullOrEmpty(assemblyPath)
                ? $"\"{assemblyPath}\" --player {ResidentArg}"
                : $"--player {ResidentArg}",
            UseShellExecute = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        try { System.Diagnostics.Process.Start(startInfo); }
        catch (Exception ex) { Log.Debug(ex, "Failed to start resident player process"); }
    }

    public static Task StopResidentAsync()
    {
        return Task.Run(() => TryForward(["--player", ShutdownArg], timeoutMs: 500));
    }

    public static void ForwardMetadata(
        string originalTitle,
        int animeId,
        string? titleRu,
        string? titleEn,
        string? episodeText)
    {
        ForwardMetadata(originalTitle, animeId, titleRu, titleEn, titleRomaji: null, episodeText);
    }

    private static string? s_lastForwardedKey;
    private static readonly Lock s_forwardGate = new();

    public static void ForwardMetadata(
        string originalTitle,
        int animeId,
        string? titleRu,
        string? titleEn,
        string? titleRomaji,
        string? episodeText)
    {
        var key = $"{originalTitle}|{animeId}|{titleRu}|{titleEn}|{titleRomaji}|{episodeText}";
        lock (s_forwardGate)
        {
            if (string.Equals(s_lastForwardedKey, key, StringComparison.Ordinal))
                return;
            s_lastForwardedKey = key;
        }

        string[] args = [
            "--player",
            UpdateMetadataArg,
            "--original-title",
            originalTitle ?? string.Empty,
            "--anime-id",
            animeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--title-ru",
            titleRu ?? string.Empty,
            "--title-en",
            titleEn ?? string.Empty,
            "--title-romaji",
            titleRomaji ?? string.Empty,
            "--episode",
            episodeText ?? string.Empty
        ];

        Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (TryForward(args, timeoutMs: 1000))
                    return;

                try { await Task.Delay(100).ConfigureAwait(false); } catch { }
            }
        });
    }
}
