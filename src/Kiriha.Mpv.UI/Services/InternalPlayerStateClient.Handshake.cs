using System.IO.Pipes;
using System.Text.Json;
using Kiriha.Core.Domain.Models.Api;
using Serilog;

namespace Kiriha.Mpv.UI.Services;

public sealed partial class InternalPlayerStateClient
{
    private async Task<bool> AttemptConnectAndHandshakeAsync(CancellationToken token)
    {
        NamedPipeClientStream? client = null;
        StreamWriter? writer = null;
        StreamReader? reader = null;

        try
        {
            client = new NamedPipeClientStream(
                ".",
                _pipeName,
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
                await writer.WriteLineAsync(handshakeJson.AsMemory(), token).ConfigureAwait(false);
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
                token.ThrowIfCancellationRequested();
                _client = client;
                _writer = writer;
                _isHandshakeConfirmed = true;
            }

            Log.Information("InternalPlayerStateClient: Connected and handshake confirmed.");

            // If handshake ack included metadata, notify listener immediately
            if (ackMsg.Metadata != null && IsCurrentConnection(client))
            {
                try { MetadataReceived?.Invoke(ackMsg.Metadata); } catch (Exception ex) { Log.Warning(ex, "Error in MetadataReceived callback"); }
            }

            // Flush any state queued while connecting
            bool shouldFlush = false;
            lock (_stateGate)
            {
                if (_pendingState != null && !ReferenceEquals(_pendingState, stateForHandshake))
                {
                    _stateToFlush = _pendingState;
                    shouldFlush = true;
                }
                else
                {
                    _stateToFlush = null;
                }
            }
            if (shouldFlush)
            {
                _ = FlushPendingStateAsync();
            }

            // Start background reader loop to listen for metadata and detect disconnects
            _ = RunReaderLoopAsync(reader, client, token);

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
}
