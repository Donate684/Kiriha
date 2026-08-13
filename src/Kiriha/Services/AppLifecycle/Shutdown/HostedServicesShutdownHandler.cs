using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class HostedServicesShutdownHandler : IShutdownHandler
{
    private readonly IEnumerable<IHostedService> _hostedServices;

    public HostedServicesShutdownHandler(IEnumerable<IHostedService> hostedServices)
    {
        _hostedServices = hostedServices;
    }

    public async Task FlushAsync()
    {
        const int timeoutMs = 2000;
        try
        {
            var operation = Task.Run(async () =>
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                foreach (var hosted in _hostedServices)
                {
                    try
                    {
                        await hosted.StopAsync(stopCts.Token);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Shutdown: hosted service {Type} StopAsync threw", hosted.GetType().Name);
                    }
                }
            });

            if (!await WaitForAsync(operation, timeoutMs + 500))
                Log.Warning("Shutdown flush: hosted services exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: hosted services stop failed");
        }
    }

    private static async Task<bool> WaitForAsync(Task operation, int timeoutMs)
    {
        try
        {
            await operation.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
