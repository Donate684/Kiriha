using System.IO.Pipes;
using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Player;
using Serilog;

namespace Kiriha.Mpv.UI.Services;

public sealed class InternalPlayerStateClient : IDisposable
{
    public event Action<PlayerMediaMetadata>? MetadataReceived;
    public Func<InternalPlayerState>? StateProvider { get; set; }

    private readonly Lock _connectionGate = new();
    private readonly Lock _stateGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private NamedPipeClientStream? _client;
    private StreamWriter? _writer;
    private InternalPlayerState? _pendingState;

    private Task? _supervisorTask;
    private readonly AsyncAutoResetEvent _reconnectTrigger = new();
    private bool _isHandshakeConfirmed;
    private bool _disposed;

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
        if (_disposed) return;
        lock (_connectionGate)
        {
            _supervisorTask ??= Task.Run(SuperviseConnectionLoopAsync);
        }
    }

    public Task ConnectAsync(int timeoutMs = 1500)
    {
        Start();
        return Task.CompletedTask;
    }

    public void Publish(InternalPlayerState state)
    {
        if (_disposed) return;

        lock (_stateGate)
        {
            _pendingState = state;
        }

        if (IsConnected)
        {
            _ = SendStateMessageAsync(state);
        }
        else
        {
            Start();
            _reconnectTrigger.Set();
        }
    }

    public void PublishClosed()
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

            if (_writeGate.Wait(300))
            {
                try
                {
                    writer.WriteLine(json);
                    writer.Flush();
                }
                finally
                {
                    _writeGate.Release();
                }
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

        while (!_cts.IsCancellationRequested && !_disposed)
        {
            bool connected = false;
            try
            {
                connected = await AttemptConnectAndHandshakeAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "InternalPlayerStateClient: Connection attempt failed");
            }

            if (connected)
            {
                failureCount = 0;
                // Wait until disconnected or cancellation requested
                await _reconnectTrigger.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            else
            {
                failureCount++;

                // If not running, wake up the main application
                if (!PlayerProcessBridge.IsMainAppRunning())
                {
                    PlayerProcessBridge.TryWakeUpMainApp();
                }

                // Exponential backoff between 400ms and 3000ms
                var backoffMs = Math.Min(400 * (1 << Math.Min(failureCount, 3)), 3000);
                await Task.WhenAny(
                    Task.Delay(backoffMs, _cts.Token),
                    _reconnectTrigger.WaitAsync(_cts.Token)
                ).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> AttemptConnectAndHandshakeAsync(CancellationToken token)
    {
        NamedPipeClientStream? client = null;
        StreamWriter? writer = null;
        StreamReader? reader = null;

        try
        {
            client = new NamedPipeClientStream(
                ".",
                InternalPlayerBridge.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            const int connectTimeoutMs = 1500;
            await client.ConnectAsync(connectTimeoutMs, token).ConfigureAwait(false);

            var encoding = new System.Text.UTF8Encoding(false);
            writer = new StreamWriter(client, encoding, 1024, leaveOpen: true) { AutoFlush = true };
            reader = new StreamReader(client, encoding, false, 1024, leaveOpen: true);

            // Prepare Handshake
            InternalPlayerState? stateForHandshake;
            lock (_stateGate)
            {
                stateForHandshake = _pendingState ?? StateProvider?.Invoke();
            }

            var handshakeMsg = new InternalPlayerIpcMessage
            {
                Type = InternalPlayerIpcMessage.TypeHandshake,
                Version = "1.0",
                State = stateForHandshake
            };
            var handshakeJson = JsonSerializer.Serialize(handshakeMsg, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);

            // Write Handshake
            await _writeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(handshakeJson).ConfigureAwait(false);
                await writer.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }

            // Await HandshakeAck with 2500ms timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(2500);

            var ackLine = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(ackLine))
            {
                Log.Warning("InternalPlayerStateClient: Handshake failed - empty response");
                DisposeStreamResources(writer, reader, client);
                return false;
            }

            ackLine = ackLine.Trim().Trim('\uFEFF');
            if (string.IsNullOrEmpty(ackLine))
            {
                Log.Warning("InternalPlayerStateClient: Handshake failed - empty response after trim");
                DisposeStreamResources(writer, reader, client);
                return false;
            }

            InternalPlayerIpcMessage? ackMsg = null;
            try
            {
                ackMsg = JsonSerializer.Deserialize(ackLine, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "InternalPlayerStateClient: Failed to parse handshake response: '{AckLine}'", ackLine);
            }

            if (ackMsg?.Type != InternalPlayerIpcMessage.TypeHandshakeAck)
            {
                Log.Warning("InternalPlayerStateClient: Unexpected handshake response: {Type}", ackMsg?.Type);
                DisposeStreamResources(writer, reader, client);
                return false;
            }

            lock (_connectionGate)
            {
                _client = client;
                _writer = writer;
                _isHandshakeConfirmed = true;
            }

            Log.Information("InternalPlayerStateClient: Connected and handshake confirmed.");

            // If handshake ack included metadata, notify listener immediately
            if (ackMsg.Metadata != null)
            {
                try { MetadataReceived?.Invoke(ackMsg.Metadata); } catch (Exception ex) { Log.Warning(ex, "Error in MetadataReceived callback"); }
            }

            // Flush any state queued while connecting
            InternalPlayerState? pending;
            lock (_stateGate)
            {
                pending = _pendingState;
            }
            if (pending != null && !ReferenceEquals(pending, stateForHandshake))
            {
                _ = SendStateMessageAsync(pending);
            }

            // Start background reader loop to listen for metadata and detect disconnects
            _ = Task.Run(() => RunReaderLoopAsync(reader, client, token), token);

            return true;
        }
        catch (OperationCanceledException)
        {
            DisposeStreamResources(writer, reader, client);
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InternalPlayerStateClient: Failed to establish handshake");
            DisposeStreamResources(writer, reader, client);
            return false;
        }
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
                    if (msg?.Type == InternalPlayerIpcMessage.TypeMetadata && msg.Metadata != null)
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
            DisposeConnection();
            _reconnectTrigger.Set();
        }
    }

    private async Task SendStateMessageAsync(InternalPlayerState state)
    {
        StreamWriter? writer;
        lock (_connectionGate)
        {
            if (!_isHandshakeConfirmed || _client?.IsConnected != true)
                return;
            writer = _writer;
        }

        if (writer is null) return;

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_connectionGate)
            {
                if (!_isHandshakeConfirmed || _client?.IsConnected != true)
                    return;
            }

            var msg = new InternalPlayerIpcMessage
            {
                Type = InternalPlayerIpcMessage.TypeState,
                State = state
            };
            var json = JsonSerializer.Serialize(msg, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
            await writer.WriteLineAsync(json).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InternalPlayerStateClient: Write failed. Connection will reset.");
            DisposeConnection();
            _reconnectTrigger.Set();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void DisposeConnection()
    {
        StreamWriter? writer;
        NamedPipeClientStream? client;

        lock (_connectionGate)
        {
            _isHandshakeConfirmed = false;
            writer = _writer;
            client = _client;
            _writer = null;
            _client = null;
        }

        DisposeStreamResources(writer, null, client);
    }

    private static void DisposeStreamResources(StreamWriter? writer, StreamReader? reader, NamedPipeClientStream? client)
    {
        try { writer?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        try { client?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            _disposed = true;
            _pendingState = null;
        }

        try { _cts.Cancel(); } catch { }
        _reconnectTrigger.Set();

        DisposeConnection();

        try { _cts.Dispose(); } catch { }
        try { _writeGate.Dispose(); } catch { }
    }

    private sealed class AsyncAutoResetEvent
    {
        private static readonly Task s_completed = Task.CompletedTask;
        private readonly Queue<TaskCompletionSource<bool>> _waits = new();
        private bool _signaled;

        public Task WaitAsync(CancellationToken token)
        {
            lock (_waits)
            {
                if (_signaled)
                {
                    _signaled = false;
                    return s_completed;
                }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (token.CanBeCanceled)
                {
                    token.Register(() =>
                    {
                        lock (_waits)
                        {
                            tcs.TrySetCanceled(token);
                        }
                    });
                }
                _waits.Enqueue(tcs);
                return tcs.Task;
            }
        }

        public void Set()
        {
            TaskCompletionSource<bool>? toRelease = null;
            lock (_waits)
            {
                while (_waits.Count > 0)
                {
                    var waiter = _waits.Dequeue();
                    if (!waiter.Task.IsCompleted)
                    {
                        toRelease = waiter;
                        break;
                    }
                }

                if (toRelease == null)
                {
                    _signaled = true;
                }
            }
            toRelease?.TrySetResult(true);
        }
    }
}
