using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Data.Core;
using Kiriha.Infrastructure.Platform;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle.Shutdown;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiriha.Tests;

internal static class PlayerIsolationTestProcess
{
    internal const string Command = "--player-isolation-test-process";
    internal const string Completed = "Standalone player initialized and shut down";

    internal static async Task<int> RunAsync(string appDirectory, string[] args)
    {
        try
        {
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", appDirectory);
            if (!PathHelper.IsPortable || Path.GetDirectoryName(PathHelper.GetDbPath()) != Path.Combine(appDirectory, "data"))
                throw new InvalidOperationException("The player test requires an isolated portable data directory.");
            if (!PlayerModeCoordinator.IsPlayerMode(args))
                throw new InvalidOperationException("Player mode was not selected.");

            using var provider = (ServiceProvider)AppStartupCoordinator.BuildServiceProvider(isPlayerMode: true);
            if (provider.GetService<IDatabaseInitializer>() != null ||
                provider.GetService<IDbContextFactory<AppDbContext>>() != null ||
                provider.GetService<IUserAnimeRepository>() != null ||
                provider.GetServices<IHostedService>().Any())
                throw new InvalidOperationException("The standalone player loaded desktop tracker services.");

            var handlers = provider.GetServices<IShutdownHandler>().ToArray();
            if (handlers.Length == 0 || handlers.Any(handler => handler is DatabaseShutdownHandler))
                throw new InvalidOperationException("Standalone player shutdown has invalid handlers.");

            using (var player = PlayerModeCoordinator.CreatePlayerViewModel(args, provider))
            {
                if (player.VideoUrl != args[1])
                    throw new InvalidOperationException("The player did not receive the requested local file.");
            }

            await new ShutdownCoordinator(provider, handlers).DrainAsync();
            if (!File.Exists(PathHelper.GetSettingsAppPath()))
                throw new InvalidOperationException("The player did not save settings to its portable directory.");
            await Console.Out.WriteLineAsync(Completed);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString());
            return 1;
        }
    }
}
