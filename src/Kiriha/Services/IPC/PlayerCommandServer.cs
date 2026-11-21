using System.IO.Pipes;
using Kiriha.Infrastructure.Player;
using Serilog;

namespace Kiriha.Services;

public sealed class PlayerCommandServer : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan CommandReadTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    private readonly Action<string[]> _handleCommand;
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

    internal PlayerCommandServer(Action<string[]> handleCommand, string pipeName, TimeSpan readTimeout)
    {
        _handleCommand = handleCommand;
        _pipeName = pipeName;
        _readTimeout = readTimeout;
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
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
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
            try
            {
                await ReadCommandAsync(pipe, ct).ConfigureAwait(false);
            }
            finally
            {
                // Release the connection even if the client kept its handle open after a read timeout.
                lock (_pipeGate)
                {
                    if (!ct.IsCancellationRequested)
                        pipe.Disconnect();
                }
            }
        }
    }

    private async Task ReadCommandAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_readTimeout);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        try
        {
            var line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(line))
                _handleCommand(PipeArgumentSerializer.Deserialize(line));
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            Log.Debug(ex, "PlayerCommandServer: ignored an incomplete command after read timeout");
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
