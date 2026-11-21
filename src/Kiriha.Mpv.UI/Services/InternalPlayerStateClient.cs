using System.IO.Pipes;
using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Player;
using Serilog;

namespace Kiriha.Mpv.UI.Services;

public sealed partial class InternalPlayerStateClient : IDisposable
{
    public event Action<PlayerMediaMetadata>? MetadataReceived;
    public Func<InternalPlayerState>? StateProvider { get; set; }

    private readonly string _pipeName;
    private readonly Lock _connectionGate = new();
    private readonly Lock _stateGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _reconnectTrigger = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationToken _shutdown;

    public InternalPlayerStateClient(string pipeName = InternalPlayerBridge.PipeName)
    {
        _pipeName = pipeName;
        _shutdown = _cts.Token;
    }

    private NamedPipeClientStream? _client;
    private StreamWriter? _writer;
    private InternalPlayerState? _pendingState;
    private InternalPlayerState? _stateToFlush;

    private Task? _supervisorTask;
    private bool _isHandshakeConfirmed;
    private volatile bool _disposed;

    public bool IsConnected
    {
        get
        {
            lock (_connectionGate)
            {
                return _client?.IsConnected == true && _isHandshakeConfirmed;
            }
        }
    }

    public void Start()
    {
        lock (_connectionGate)
        {
            if (!_disposed)
                _supervisorTask ??= Task.Run(SuperviseConnectionLoopAsync, _shutdown);
        }
    }

    public void Publish(InternalPlayerState state)
    {
        if (_disposed) return;

        lock (_stateGate)
        {
            _pendingState = state;
            _stateToFlush = state;
        }

        if (IsConnected)
        {
            _ = FlushPendingStateAsync();
        }
        else
        {
            Start();
            SignalSupervisor();
        }
    }

    public async Task PublishClosedAsync(CancellationToken ct)
    {
        if (_disposed) return;

        StreamWriter? writer;
        lock (_connectionGate)
        {
            if (!_isHandshakeConfirmed || _client?.IsConnected != true)
                return;
            writer = _writer;
        }

        if (writer is null) return;

        try
        {
            var msg = new InternalPlayerIpcMessage
            {
                Type = InternalPlayerIpcMessage.TypeClosed,
                State = new InternalPlayerState { IsClosed = true }
            };
            var json = JsonSerializer.Serialize(msg, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);

            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InternalPlayerStateClient: Error publishing closed state");
        }
    }

    private async Task SuperviseConnectionLoopAsync()
    {
        int failureCount = 0;

        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                if (IsConnected)
                {
                    await _reconnectTrigger.WaitAsync(_shutdown).ConfigureAwait(false);
                    continue;
                }

                DisposeConnection();
                if (await AttemptConnectAndHandshakeAsync(_shutdown).ConfigureAwait(false))
                {
                    failureCount = 0;
                    continue;
                }

                if (_pipeName == InternalPlayerBridge.PipeName && !PlayerProcessBridge.IsMainAppRunning())
                    PlayerProcessBridge.TryWakeUpMainApp();

                var backoffMs = Math.Min(400 * (1 << Math.Min(++failureCount, 3)), 3000);
                await _reconnectTrigger.WaitAsync(backoffMs, _shutdown).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "InternalPlayerStateClient: Connection attempt failed");
            }

        }
    }

    private void SignalSupervisor()
    {
        try { _reconnectTrigger.Release(); }
        catch (SemaphoreFullException) { /* A wake-up is already queued. */ }
    }

    private async Task RunReaderLoopAsync(StreamReader reader, NamedPipeClientStream client, CancellationToken token)
    {
        try
        {
            while (client.IsConnected && !token.IsCancellationRequested && !_disposed)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(line))
                    break;

                line = line.Trim().Trim('\uFEFF');
                if (string.IsNullOrEmpty(line))
                    continue;

                try
                {
                    var msg = JsonSerializer.Deserialize(line, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
                    if (msg?.Type == InternalPlayerIpcMessage.TypeMetadata && msg.Metadata != null && IsCurrentConnection(client))
                    {
                        MetadataReceived?.Invoke(msg.Metadata);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "InternalPlayerStateClient: Error parsing server message");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug(ex, "InternalPlayerStateClient: Reader loop terminated");
        }
        finally
        {
            Log.Information("InternalPlayerStateClient: Connection to server lost. Will reconnect.");
            if (DisposeConnection(client))
                SignalSupervisor();
            reader.Dispose();
        }
    }

    private async Task FlushPendingStateAsync()
    {
        try
        {
            await _writeGate.WaitAsync(_shutdown).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }

        NamedPipeClientStream? sendingClient = null;
        try
        {
            while (!_disposed)
            {
                InternalPlayerState? stateToWrite;
                lock (_stateGate)
                {
                    stateToWrite = _stateToFlush;
                    _stateToFlush = null;
                }

                if (stateToWrite is null)
                    return;

                StreamWriter? writer;
                lock (_connectionGate)
                {
                    if (!_isHandshakeConfirmed || _client?.IsConnected != true)
                        return;
                    sendingClient = _client;
                    writer = _writer;
                }

                if (writer is null)
                    return;

                var msg = new InternalPlayerIpcMessage
                {
                    Type = InternalPlayerIpcMessage.TypeState,
                    State = stateToWrite
                };
                var json = JsonSerializer.Serialize(msg, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
                await writer.WriteLineAsync(json.AsMemory(), _shutdown).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InternalPlayerStateClient: Write failed. Connection will reset.");
            if (sendingClient != null && DisposeConnection(sendingClient))
                SignalSupervisor();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private bool IsCurrentConnection(NamedPipeClientStream client)
    {
        lock (_connectionGate)
            return !_disposed && ReferenceEquals(_client, client);
    }

    private bool DisposeConnection(NamedPipeClientStream? expected = null)
    {
        StreamWriter? writer;
        NamedPipeClientStream? client;

        lock (_connectionGate)
        {
            if (expected != null && !ReferenceEquals(_client, expected)) return false;

            _isHandshakeConfirmed = false;
            writer = _writer;
            client = _client;
            _writer = null;
            _client = null;
        }

        DisposeStreamResources(writer, null, client);
        return client != null;
    }

    private static void DisposeStreamResources(StreamWriter? writer, StreamReader? reader, NamedPipeClientStream? client)
    {
        try { client?.Dispose(); } catch { }
        try { writer?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingState = null;
            _stateToFlush = null;
        }

        try { _cts.Cancel(); } catch { }
        SignalSupervisor();

        DisposeConnection();

        try { _cts.Dispose(); } catch { }
    }
}
