using System.IO.Pipes;
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
            writer.Flush();
            try { client.WaitForPipeDrain(); } catch { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long s_lastWakeupAttemptTicks;
    private static readonly TimeSpan WakeupCooldown = TimeSpan.FromSeconds(15);

    public static bool IsMainAppRunning()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (System.Threading.Mutex.TryOpenExisting(Kiriha.Core.Domain.Constants.AppConstants.System.MutexName, out var mutex))
                {
                    mutex.Dispose();
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    public static bool TryWakeUpMainApp()
    {
        if (IsMainAppRunning())
            return false;

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref s_lastWakeupAttemptTicks);
        if (now - last < (long)WakeupCooldown.TotalMilliseconds)
            return false;

        Interlocked.Exchange(ref s_lastWakeupAttemptTicks, now);

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)) return false;

        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var isDotnet = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = processPath,
            Arguments = isDotnet && !string.IsNullOrEmpty(assemblyPath)
                ? $"\"{assemblyPath}\" {Kiriha.Core.Domain.Constants.AppConstants.System.MinimizedArg}"
                : Kiriha.Core.Domain.Constants.AppConstants.System.MinimizedArg,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
        };

        try
        {
            System.Diagnostics.Process.StartAndForget(startInfo);
            Log.Information("PlayerProcessBridge: Woke up main application with minimized flag");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "PlayerProcessBridge: Failed to wake up main application");
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
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.KillOnParentExit = true;
        }

        try { System.Diagnostics.Process.StartAndForget(startInfo); }
        catch (Exception ex) { Log.Debug(ex, "Failed to start resident player process"); }
    }

    public static Task StopResidentAsync()
    {
        lock (s_forwardGate)
        {
            s_lastForwardedKey = null;
        }
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
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (TryForward(args, timeoutMs: 1000))
                {
                    lock (s_forwardGate)
                    {
                        s_lastForwardedKey = key;
                    }
                    return;
                }

                try { await Task.Delay(200).ConfigureAwait(false); } catch { }
            }
        });
    }
}
