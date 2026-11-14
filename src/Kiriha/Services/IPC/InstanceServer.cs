using System.IO.Pipes;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Infrastructure.Player;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Services.Data.Settings;
using Kiriha.Views.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
namespace Kiriha.Services;
public class InstanceServer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Lock _pipeGate = new();
    private NamedPipeServerStream? _currentPipe;
    public InstanceServer(IServiceProvider serviceProvider, IUiDispatcher uiDispatcher)
    {
        _serviceProvider = serviceProvider;
        _uiDispatcher = uiDispatcher;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    "Kiriha.InstanceServer",
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                lock (_pipeGate) _currentPipe = pipeServer;
                await pipeServer.WaitForConnectionAsync(stoppingToken);

                var connectedPipe = pipeServer;
                pipeServer = null;
                lock (_pipeGate)
                {
                    if (ReferenceEquals(_currentPipe, connectedPipe))
                        _currentPipe = null;
                }

                _ = HandleClientAsync(connectedPipe, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Log.Error(ex, "InstanceServer error");
                await Task.Delay(1000, stoppingToken);
            }
            finally
            {
                lock (_pipeGate)
                {
                    if (ReferenceEquals(_currentPipe, pipeServer))
                        _currentPipe = null;
                }
                pipeServer?.Dispose();
            }
        }
    }
    private async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken stoppingToken)
    {
        try
        {
            using (pipeServer)
            using (var reader = new System.IO.StreamReader(pipeServer))
            {
                var line = await reader.ReadLineAsync(stoppingToken);
                if (!string.IsNullOrEmpty(line))
                {
                    var args = PipeArgumentSerializer.Deserialize(line);
                    HandleArguments(args);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug(ex, "InstanceServer: failed reading client command");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        var stopTask = base.StopAsync(cancellationToken);
        lock (_pipeGate)
        {
            _currentPipe?.Dispose();
            _currentPipe = null;
        }
        return stopTask;
    }
    private void HandleArguments(string[] args)
    {
        int playerArgIndex = Array.FindIndex(args, arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));
        string? candidateMediaUrl = null;
        if (playerArgIndex >= 0)
        {
            candidateMediaUrl = playerArgIndex + 1 < args.Length && !args[playerArgIndex + 1].StartsWith("--")
                ? args[playerArgIndex + 1]
                : string.Empty;
        }
        else
        {
            candidateMediaUrl = args.FirstOrDefault(a => !a.StartsWith("--") && (
                a.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                a.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                a.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                a.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                a.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                (Uri.TryCreate(a, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) ||
                File.Exists(a)));
        }

        if (playerArgIndex >= 0 || candidateMediaUrl != null)
        {
            string videoUrl = candidateMediaUrl ?? string.Empty;
            var torrServer = _serviceProvider.GetService<ITorrServerService>();
            if (torrServer != null && !torrServer.IsRunning && Utils.PlayerProcessHelper.IsTorrentStream(videoUrl, args))
            {
                _ = torrServer.EnsureRunningAsync();
            }
            _uiDispatcher.Post(() =>
            {
                var metadataResolver = _serviceProvider.GetRequiredService<Kiriha.Mpv.UI.Services.Player.IPlayerMediaMetadataResolver>();
                var settingsService = _serviceProvider.GetService<SettingsService>();
                var cliMetadata = Kiriha.Services.AppLifecycle.PlayerModeCoordinator.ExtractMetadataFromArgs(args);
                var headers = Kiriha.Services.AppLifecycle.PlayerModeCoordinator.ExtractHeadersFromArgs(args);
                string? subtitleUrl = null;
                var subIndex = Array.FindIndex(args, a => a.Equals("--sub-file", StringComparison.OrdinalIgnoreCase) || a.Equals("--subtitle", StringComparison.OrdinalIgnoreCase));
                if (subIndex >= 0 && subIndex + 1 < args.Length && !args[subIndex + 1].StartsWith("--"))
                {
                    subtitleUrl = args[subIndex + 1];
                }

                if (settingsService?.Current.Player.SingleWindow == true
                    && Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var existingWindow = desktop.Windows.OfType<PlayerWindow>().LastOrDefault();
                    if (existingWindow?.DataContext is PlayerViewModel existingVm)
                    {
                        if (!string.IsNullOrWhiteSpace(videoUrl))
                        {
                            existingVm.LoadVideo(videoUrl, subtitleUrl, cliMetadata, headers);
                        }
                        existingWindow.Show();
                        existingWindow.Activate();
                        return;
                    }
                }
                var initialMeta = cliMetadata ?? metadataResolver.Resolve(videoUrl);
                var playerVm = new PlayerViewModel(videoUrl, initialMeta, metadataResolver, settingsService, _serviceProvider.GetRequiredService<ILocalizer>(), _serviceProvider.GetService<ITorrServerService>(), subtitleUrl, headers);
                var window = new PlayerWindow(settingsService!) { DataContext = playerVm };
                window.Show();
                window.Activate();
            });
        }
        else
        {
            _uiDispatcher.Post(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    if (desktop.MainWindow != null)
                    {
                        desktop.MainWindow.ShowInTaskbar = true;
                        desktop.MainWindow.Show();
                        desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                        desktop.MainWindow.Activate();
                    }
                }
            });
        }
    }
}
