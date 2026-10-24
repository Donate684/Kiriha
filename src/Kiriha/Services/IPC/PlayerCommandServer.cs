using System.IO.Pipes;
using Kiriha.Infrastructure.Player;
using Serilog;

namespace Kiriha.Services;

public sealed class PlayerCommandServer : IDisposable
{
    private readonly Action<string[]> _handleCommand;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _pipeGate = new();
    private NamedPipeServerStream? _currentPipe;
    private Task? _loopTask;

    public PlayerCommandServer(Action<string[]> handleCommand)
    {
        _handleCommand = handleCommand;
    }

    public void Start()
    {
        _loopTask ??= Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PlayerProcessBridge.PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                lock (_pipeGate) _currentPipe = pipe;

                await pipe.WaitForConnectionAsync(_cts.Token);

                var connectedPipe = pipe;
                pipe = null;
                lock (_pipeGate)
                {
                    if (ReferenceEquals(_currentPipe, connectedPipe))
                        _currentPipe = null;
                }

                _ = HandleClientAsync(connectedPipe);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PlayerCommandServer: command loop failed");
            }
            finally
            {
                lock (_pipeGate)
                {
                    if (ReferenceEquals(_currentPipe, pipe))
                        _currentPipe = null;
                }
                pipe?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe))
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (string.IsNullOrWhiteSpace(line))
                    return;

                var args = PipeArgumentSerializer.Deserialize(line);
                _handleCommand(args);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug(ex, "PlayerCommandServer: failed reading client command");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_pipeGate)
        {
            _currentPipe?.Dispose();
            _currentPipe = null;
        }

        try
        {
            if (_loopTask?.Wait(500) != false)
            {
                _cts.Dispose();
            }
        }
        catch
        {
            _cts.Dispose();
        }
    }
}
