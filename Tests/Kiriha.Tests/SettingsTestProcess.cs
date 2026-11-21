using Kiriha.Core.Abstractions.Services;
using Kiriha.Infrastructure.Settings;

namespace Kiriha.Tests;

internal static class SettingsTestProcess
{
    public const string Command = "--settings-test-process";
    public const string Ready = "Settings change prepared";
    public const string Flushed = "Temporary settings written before replacement";

    internal static int Run(string directory, string operation)
    {
        try
        {
            using var settings = new SettingsService(directory, _ =>
            {
                if (operation == "interrupt")
                {
                    Signal(Flushed);
                    Console.ReadLine();
                }
            });

            settings.Update(config =>
            {
                switch (operation)
                {
                    case "volume": config.Player.Volume = 47; break;
                    case "speed": config.Player.PlaybackSpeed = 1.5; break;
                    case "interrupt": config.Player.Volume = 19; break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }
            }, SettingsSection.Player, save: false);

            Signal(Ready);
            if (Console.ReadLine() != "save")
                return 2;

            settings.SaveImmediate();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Signal(string message)
    {
        Console.WriteLine(message);
        Console.Out.Flush();
    }
}
