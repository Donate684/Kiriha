using System.IO.Pipes;
using System.Text;
using Serilog;

namespace Kiriha.Infrastructure.Player;

public static partial class PlayerProcessBridge
{
    public const string MutexName = "Kiriha.PlayerInstance";
    public const string PipeName = "Kiriha.PlayerInstance";
    public const string ResidentArg = "--player-resident";
    public const string ShutdownArg = "--shutdown-player";
    public const string ConfirmShutdownArg = "--confirm-player-shutdown";
    public const string UpdateMetadataArg = "--player-update-metadata";
    public const string PlaybackRequestArg = "--playback-request";

    public static bool IsResident(string[] args) =>
        Array.Exists(args, arg => arg.Equals(ResidentArg, StringComparison.OrdinalIgnoreCase));

    private const int ForwardTimeoutMs = 3000;
    private static readonly TimeSpan ShutdownConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ShutdownConfirmationTimeout = TimeSpan.FromSeconds(15);

    public static bool TryForward(string[] args, int timeoutMs = ForwardTimeoutMs) =>
        TryForwardAsync(args, timeoutMs).GetAwaiter().GetResult();

    public static Task<bool> TryForwardAsync(string[] args, int timeoutMs = ForwardTimeoutMs, CancellationToken ct = default) =>
        SendCommandAsync(PipeName, args, TimeSpan.FromMilliseconds(timeoutMs), ct);

    internal static async Task<bool> SendCommandAsync(string pipeName, string[] args, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var frame = Encoding.UTF8.GetBytes(PipeArgumentSerializer.Serialize(args) + "\n");
            await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
            await client.WriteAsync(frame.AsMemory(), deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ct.ThrowIfCancellationRequested();
            Log.Debug(ex, "Player command pipe unavailable");
            return false;
        }
    }

    private static long s_lastWakeupAttemptTicks;
    private static readonly TimeSpan WakeupCooldown = TimeSpan.FromSeconds(15);

    public static bool IsPlayerInstanceRunning()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (System.Threading.Mutex.TryOpenExisting(MutexName, out var mutex))
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

    public static async Task<PlayerShutdownResult> StopResidentAsync(CancellationToken ct = default)
    {
        lock (s_forwardGate)
        {
            s_lastForwardedKey = null;
        }
        ct.ThrowIfCancellationRequested();
        if (!IsPlayerInstanceRunning())
            return PlayerShutdownResult.Unavailable;

        var result = await SendShutdownCommandAsync(
            PipeName, ShutdownConnectTimeout, ShutdownConfirmationTimeout, ct).ConfigureAwait(false);
        return result == PlayerShutdownResult.Unavailable && IsPlayerInstanceRunning()
            ? PlayerShutdownResult.Unconfirmed
            : result;
    }

    internal static async Task<PlayerShutdownResult> SendShutdownCommandAsync(
        string pipeName, TimeSpan connectTimeout, TimeSpan confirmationTimeout, CancellationToken ct)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connect.CancelAfter(connectTimeout);
            try
            {
                await client.ConnectAsync(connect.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return PlayerShutdownResult.Unavailable;
            }
            catch (UnauthorizedAccessException ex)
            {
                // An older resident accepts write-only connections.
                Log.Debug(ex, "Player shutdown confirmation pipe unavailable, using legacy command");
                return await SendCommandAsync(pipeName, ["--player", ShutdownArg], connectTimeout, ct).ConfigureAwait(false)
                    ? PlayerShutdownResult.Unconfirmed
                    : PlayerShutdownResult.Unavailable;
            }
            catch (IOException ex)
            {
                ct.ThrowIfCancellationRequested();
                Log.Debug(ex, "Player shutdown pipe unavailable");
                return PlayerShutdownResult.Unavailable;
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(confirmationTimeout);
        try
        {
            var frame = Encoding.UTF8.GetBytes(PipeArgumentSerializer.Serialize(["--player", ShutdownArg, ConfirmShutdownArg]) + "\n");
            await client.WriteAsync(frame.AsMemory(), deadline.Token).ConfigureAwait(false);
            var response = new byte[1];
            await client.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
            return response[0] switch
            {
                (byte)PlayerShutdownResult.Completed => PlayerShutdownResult.Completed,
                (byte)PlayerShutdownResult.Failed => PlayerShutdownResult.Failed,
                _ => PlayerShutdownResult.Unconfirmed
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return PlayerShutdownResult.Unconfirmed;
        }
        catch (IOException ex)
        {
            ct.ThrowIfCancellationRequested();
            Log.Debug(ex, "Player disconnected before confirming shutdown");
            return PlayerShutdownResult.Unconfirmed;
        }
    }

    private static string? s_lastForwardedKey;
    private static readonly Lock s_forwardGate = new();

    public static void ForwardMetadata(
        string originalTitle,
        int animeId,
        string? titleRu,
        string? titleEn,
        string? titleRomaji,
        string? episodeText,
        string? torrentHash = null)
    {
        if (!IsPlayerInstanceRunning())
            return;

        var key = $"{originalTitle}|{animeId}|{titleRu}|{titleEn}|{titleRomaji}|{episodeText}|{torrentHash}";
        lock (s_forwardGate)
        {
            if (string.Equals(s_lastForwardedKey, key, StringComparison.Ordinal))
                return;
        }

        var argsList = new List<string>
        {
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
        };

        if (!string.IsNullOrEmpty(torrentHash))
        {
            argsList.Add("--torrent-hash");
            argsList.Add(torrentHash);
        }

        string[] args = [.. argsList];

        Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (await TryForwardAsync(args, timeoutMs: 1000).ConfigureAwait(false))
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
