using Kiriha.Core.Constants;
using System;
using System.IO;
using Serilog;

namespace Kiriha;

partial class Program
{
    private static void InitializeLogging(string[] args)
    {
        bool enableLogging = false;
        try
        {
            var settingsPath = Kiriha.Core.Platform.PathHelper.GetSettingsPath();
            if (File.Exists(settingsPath))
            {
                var content = File.ReadAllText(settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(content);
                enableLogging = doc.RootElement
                    .GetProperty("System")
                    .GetProperty("EnableLogging")
                    .GetBoolean();
            }
        }
        catch { }

        string logTemplate = Path.Combine(Kiriha.Core.Platform.PathHelper.GetLogsPath(), "kiriha-.txt");

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console();

        if (enableLogging)
        {
            loggerConfig.WriteTo.File(logTemplate, rollingInterval: RollingInterval.Day);
        }

        Log.Logger = loggerConfig.CreateLogger();

        Log.Information(Kiriha.Core.Constants.AppConstants.System.AppStartedLog);
        // Mask OAuth callback parameters (code, token, refresh) before logging command-line args.
        Log.Information("Arguments: {Args}", MaskSensitiveArgs(args));
    }
}
