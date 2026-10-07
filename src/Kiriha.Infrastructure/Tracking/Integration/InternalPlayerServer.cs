using System.IO.Pipes;
using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Player;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Infrastructure.Tracking.Integration;

public class InternalPlayerServer : BackgroundService, IInternalPlayerServer
{
    public event EventHandler<InternalPlayerState>? PlayerStateChanged;

    private sealed class ClientSession
    {
        public StreamWriter Writer { get; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public ClientSession(StreamWriter writer) => Writer = writer;
    }

    private readonly Lock _clientsGate = new();
    private readonly List<ClientSession> _clients = new();

    private readonly Lock _pipeGate = new();
    private readonly HashSet<NamedPipeServerStream> _currentPipes = new();

    private readonly Lock _metadataGate = new();
    private readonly string _pipeName;

    public InternalPlayerServer(string pipeName = InternalPlayerBridge.PipeName)
    {
        _pipeName = pipeName;
    }

    private PlayerMediaMetadata? _latestMetadata;

    public async Task SendMetadataAsync(PlayerMediaMetadata metadata, CancellationToken cancellationToken = default)
    {
        lock (_metadataGate)
        {
            _latestMetadata = metadata;
        }

        var msg = new InternalPlayerIpcMessage
        {
            Type = InternalPlayerIpcMessage.TypeMetadata,
            Metadata = metadata
        };
        var json = JsonSerializer.Serialize(msg, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);

        List<ClientSession> clientsCopy;
        lock (_clientsGate)
        {
            clientsCopy = [.. _clients];
        }

        foreach (var client in clientsCopy)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await SendToSessionAsync(client, json).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                lock (_pipeGate)
                {
                    _currentPipes.Add(pipeServer);
                }

                Log.Debug("InternalPlayerServer: Waiting for player connection...");
                await pipeServer.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                Log.Information("InternalPlayerServer: Player connected.");

                _ = HandleClientAsync(pipeServer, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                pipeServer?.Dispose();
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                pipeServer?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InternalPlayerServer: Error accepting pipe connection");
                pipeServer?.Dispose();
                try { await Task.Delay(500, stoppingToken).ConfigureAwait(false); } catch { }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        var encoding = new System.Text.UTF8Encoding(false);
        var writer = new StreamWriter(pipe, encoding, 1024, leaveOpen: true) { AutoFlush = true };
        var reader = new StreamReader(pipe, encoding, false, 1024, leaveOpen: true);
        var session = new ClientSession(writer);

        lock (_clientsGate)
        {
            _clients.Add(session);
        }

        try
        {
            while (pipe.IsConnected && !stoppingToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(stoppingToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(line))
                    break;

                line = line.Trim().Trim('\uFEFF');
                if (string.IsNullOrEmpty(line))
                    continue;

                await ProcessIncomingMessageAsync(line, session).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InternalPlayerServer: Client session ended with exception");
        }
        finally
        {
            lock (_clientsGate)
            {
                _clients.Remove(session);
            }
            lock (_pipeGate)
            {
                _currentPipes.Remove(pipe);
            }

            try { session.Writer.Dispose(); } catch { }
            try { reader.Dispose(); } catch { }
            try { pipe.Dispose(); } catch { }

            bool noClientsLeft;
            lock (_clientsGate)
            {
                noClientsLeft = _clients.Count == 0;
            }

            if (noClientsLeft)
            {
                Log.Information("InternalPlayerServer: All players disconnected. Clearing media.");
                PlayerStateChanged?.Invoke(this, new InternalPlayerState { IsClosed = true });
            }
        }
    }

    private async Task ProcessIncomingMessageAsync(string line, ClientSession session)
    {
        InternalPlayerIpcMessage? message = null;
        try
        {
            message = JsonSerializer.Deserialize(line, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
        }
        catch
        {
            // Not a typed message, fallback to state
        }

        if (message != null && !string.IsNullOrEmpty(message.Type))
        {
            switch (message.Type)
            {
                case InternalPlayerIpcMessage.TypeHandshake:
                    PlayerMediaMetadata? initialMeta = null;
                    lock (_metadataGate)
                    {
                        if (_latestMetadata != null && (message.State == null || string.IsNullOrEmpty(message.State.OriginalTitle) ||
                            string.Equals(_latestMetadata.OriginalTitle, message.State.OriginalTitle, StringComparison.OrdinalIgnoreCase)))
                        {
                            initialMeta = _latestMetadata;
                        }
                    }

                    var ack = new InternalPlayerIpcMessage
                    {
                        Type = InternalPlayerIpcMessage.TypeHandshakeAck,
                        Version = "1.0",
                        Metadata = initialMeta
                    };
                    var ackJson = JsonSerializer.Serialize(ack, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
                    await SendToSessionAsync(session, ackJson).ConfigureAwait(false);

                    if (message.State != null)
                    {
                        PlayerStateChanged?.Invoke(this, message.State);
                    }
                    break;

                case InternalPlayerIpcMessage.TypeState:
                    if (message.State != null)
                    {
                        PlayerStateChanged?.Invoke(this, message.State);
                    }
                    break;

                case InternalPlayerIpcMessage.TypeClosed:
                    PlayerStateChanged?.Invoke(this, new InternalPlayerState { IsClosed = true });
                    break;

                case InternalPlayerIpcMessage.TypePing:
                    var pong = new InternalPlayerIpcMessage { Type = InternalPlayerIpcMessage.TypePong };
                    await SendToSessionAsync(session, JsonSerializer.Serialize(pong, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage)).ConfigureAwait(false);
                    break;
            }
            return;
        }

        // Backward compatibility: raw InternalPlayerState JSON
        try
        {
            var state = JsonSerializer.Deserialize(line, InternalPlayerStateJsonContext.Default.InternalPlayerState);
            if (state != null)
            {
                PlayerStateChanged?.Invoke(this, state);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "InternalPlayerServer: Failed to parse IPC message");
        }
    }

    private static async Task SendToSessionAsync(ClientSession session, string json)
    {
        try
        {
            await session.WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await session.Writer.WriteLineAsync(json).ConfigureAwait(false);
                await session.Writer.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                session.WriteLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InternalPlayerServer: Failed to write to client");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        var stopTask = base.StopAsync(cancellationToken);

        lock (_pipeGate)
        {
            foreach (var pipe in _currentPipes)
            {
                try { pipe.Dispose(); } catch { }
            }
            _currentPipes.Clear();
        }

        lock (_clientsGate)
        {
            _clients.Clear();
        }

        return stopTask;
    }
}
