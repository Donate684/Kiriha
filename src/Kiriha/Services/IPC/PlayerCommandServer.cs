using System.IO.Pipes;
using Kiriha.Infrastructure.Player;
using Serilog;

namespace Kiriha.Services;

public sealed class PlayerCommandServer : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan CommandReadTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    private readonly Action<string[]> _handleCommand;
    private readonly Func<Task<bool>>? _prepareShutdown;
    private readonly Action? _finishShutdown;
    private readonly Func<string[], CancellationToken, Task<PlayerLaunchStatus>>? _handlePlayback;
    private readonly string _pipeName;
    private readonly TimeSpan _readTimeout;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _pipeGate = new();
    private NamedPipeServerStream? _currentPipe;
    private Task? _loopTask;
    private Task? _stopTask;

    public PlayerCommandServer(Action<string[]> handleCommand)
        : this(handleCommand, PlayerProcessBridge.PipeName, CommandReadTimeout)
    {
    }

    internal PlayerCommandServer(Action<string[]> handleCommand, string pipeName, TimeSpan readTimeout,
        Func<Task<bool>>? prepareShutdown = null, Action? finishShutdown = null,
        Func<string[], CancellationToken, Task<PlayerLaunchStatus>>? handlePlayback = null)
    {
        _handleCommand = handleCommand;
        _pipeName = pipeName;
        _readTimeout = readTimeout;
        _prepareShutdown = prepareShutdown;
        _finishShutdown = finishShutdown;
        _handlePlayback = handlePlayback;
    }

    internal PlayerCommandServer(Action<string[]> handleCommand, Func<Task<bool>> prepareShutdown, Action finishShutdown,
        Func<string[], CancellationToken, Task<PlayerLaunchStatus>> handlePlayback)
        : this(handleCommand, PlayerProcessBridge.PipeName, CommandReadTimeout, prepareShutdown, finishShutdown, handlePlayback)
    {
    }

    public void Start()
    {
        lock (_pipeGate)
        {
            if (_stopTask == null)
                _loopTask ??= Task.Run(() => RunAsync(_cts.Token), _cts.Token);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                lock (_pipeGate)
                {
                    ct.ThrowIfCancellationRequested();
                    _currentPipe = pipe;
                }

                await ServeConnectionsAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ct.IsCancellationRequested && ex is OperationCanceledException or ObjectDisposedException or IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PlayerCommandServer: command loop failed");
                try { await Task.Delay(RetryDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
            finally
            {
                lock (_pipeGate)
                    _currentPipe = null;
            }
        }
    }

    private async Task ServeConnectionsAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            bool shutdown = false;
            try
            {
                shutdown = await ReadCommandAsync(pipe, ct).ConfigureAwait(false);
                if (shutdown)
                    return;
            }
            finally
            {
                // Release the connection even if the client kept its handle open after a read timeout.
                lock (_pipeGate)
                {
                    if (!shutdown && !ct.IsCancellationRequested)
                        pipe.Disconnect();
                }
            }
        }
    }

    private async Task<bool> ReadCommandAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_readTimeout);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        try
        {
            var line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var args = PipeArgumentSerializer.Deserialize(line);
            if (PlayerPlaybackProtocol.IsPlaybackCommand(args))
            {
                if (_handlePlayback != null)
                {
                    var result = await _handlePlayback(args, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                    await ReplyAsync(pipe, (byte)result, ct).ConfigureAwait(false);
                }
                return false;
            }
            if (_prepareShutdown != null && args.Contains(PlayerProcessBridge.ShutdownArg, StringComparer.OrdinalIgnoreCase))
            {
                await CompleteShutdownAsync(pipe, args, ct).ConfigureAwait(false);
                return true;
            }
            _handleCommand(args);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            Log.Debug(ex, "PlayerCommandServer: ignored an incomplete command after read timeout");
        }
        return false;
    }

    private async Task CompleteShutdownAsync(NamedPipeServerStream pipe, string[] args, CancellationToken ct)
    {
        var result = PlayerShutdownResult.Failed;
        try
        {
            if (_prepareShutdown != null && await _prepareShutdown().ConfigureAwait(false))
                result = PlayerShutdownResult.Completed;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PlayerCommandServer: player cleanup failed");
        }

        try
        {
            if (args.Contains(PlayerProcessBridge.ConfirmShutdownArg, StringComparer.OrdinalIgnoreCase))
                await ReplyAsync(pipe, (byte)result, ct).ConfigureAwait(false);
        }
        finally
        {
            _finishShutdown?.Invoke();
        }
    }

    private async Task ReplyAsync(NamedPipeServerStream pipe, byte status, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_readTimeout);
        byte[] response = [status];
        try
        {
            await pipe.WriteAsync(response.AsMemory(), deadline.Token).ConfigureAwait(false);
            // Disconnect discards unread bytes on Windows. Keep the pipe until the requester closes it.
            if (await pipe.ReadAsync(response.AsMemory(), deadline.Token).ConfigureAwait(false) != 0)
                Log.Debug("PlayerCommandServer: ignored extra data after confirmation");
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Log.Debug(ex, "PlayerCommandServer: requester disconnected before reading the confirmation");
        }
    }

    public void Dispose() => _ = DisposeAsync();

    public ValueTask DisposeAsync()
    {
        lock (_pipeGate)
        {
            if (_stopTask == null)
            {
                _cts.Cancel();
                _currentPipe?.Dispose();
                _stopTask = FinishAsync(_loopTask);
            }
            return new ValueTask(_stopTask);
        }
    }

    private async Task FinishAsync(Task? loop)
    {
        try
        {
            if (loop != null)
                await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
