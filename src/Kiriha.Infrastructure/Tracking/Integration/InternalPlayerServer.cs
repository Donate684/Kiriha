using System.IO.Pipes;
using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Player;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Infrastructure.Tracking.Integration;

public partial class InternalPlayerServer : BackgroundService, IInternalPlayerServer
{
    public event EventHandler<InternalPlayerState>? PlayerStateChanged;

    private sealed class ClientSession(StreamWriter writer, long connectionOrder)
    {
        public StreamWriter Writer { get; } = writer;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public long ConnectionOrder { get; } = connectionOrder;
        public bool IsClosed { get; set; }
    }

    private readonly Lock _clientsGate = new();
    private readonly List<ClientSession> _clients = new();

    private readonly Lock _pipeGate = new();
    private readonly HashSet<NamedPipeServerStream> _currentPipes = new();

    private readonly Lock _stateGate = new();
    private readonly Lock _publicationGate = new();
    private readonly string _pipeName;

    public InternalPlayerServer(string pipeName = InternalPlayerBridge.PipeName)
    {
        _pipeName = pipeName;
    }

    private PlayerMediaMetadata? _latestMetadata;
    private PlayerMediaMetadata? _pendingPlaybackMetadata;
    private bool _sendLegacyMetadataOnAccept;
    private InternalPlayerState? _latestState;
    private ClientSession? _activeSession;
    private long _connectionOrder;
    private long _latestOwnerOrder;

    public bool HasConnectedClients
    {
        get
        {
            lock (_clientsGate)
            {
                return _clients.Count > 0;
            }
        }
    }

    public InternalPlayerState? LatestState
    {
        get
        {
            lock (_stateGate)
            {
                return _latestState;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clientTasks = new List<Task>();
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

                clientTasks.RemoveAll(task => task.IsCompleted);
                clientTasks.Add(HandleClientAsync(pipeServer, stoppingToken));
            }
            catch (OperationCanceledException)
            {
                if (pipeServer != null) await DisposePipeAsync(pipeServer).ConfigureAwait(false);
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                if (pipeServer != null) await DisposePipeAsync(pipeServer).ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InternalPlayerServer: Error accepting pipe connection");
                if (pipeServer != null) await DisposePipeAsync(pipeServer).ConfigureAwait(false);
                try { await Task.Delay(500, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            }
        }

        await Task.WhenAll(clientTasks).ConfigureAwait(false);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        var encoding = new System.Text.UTF8Encoding(false);
        var writer = new StreamWriter(pipe, encoding, 1024, leaveOpen: true) { AutoFlush = true };
        var reader = new StreamReader(pipe, encoding, false, 1024, leaveOpen: true);
        var session = new ClientSession(writer, Interlocked.Increment(ref _connectionOrder));

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

                await ProcessIncomingMessageAsync(line, session, stoppingToken).ConfigureAwait(false);
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
            CloseSession(session);
            lock (_clientsGate)
            {
                _clients.Remove(session);
            }
            try { await session.Writer.DisposeAsync().ConfigureAwait(false); } catch { }
            try { reader.Dispose(); } catch { }
            await DisposePipeAsync(pipe).ConfigureAwait(false);
        }
    }

    private async ValueTask DisposePipeAsync(NamedPipeServerStream pipe)
    {
        lock (_pipeGate)
            _currentPipes.Remove(pipe);

        await pipe.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<bool> SendToSessionAsync(ClientSession session, string json, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var writeToken = timeout.Token;
        try
        {
            await session.WriteLock.WaitAsync(writeToken).ConfigureAwait(false);
            try
            {
                await session.Writer.WriteLineAsync(json.AsMemory(), writeToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                session.WriteLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InternalPlayerServer: Failed to write to client");
            return false;
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

        return stopTask;
    }
}
