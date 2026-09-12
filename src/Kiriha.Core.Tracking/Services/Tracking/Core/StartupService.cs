using System;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Core.Tracking.Core;

[Obsolete("Inject and use IStartupManager instead.")]
public static class StartupService
{
    private static IStartupManager? _startupManager;

    public static void Initialize(IStartupManager startupManager) => _startupManager = startupManager;

    public static void EnableStartup(bool launchMinimized) => _startupManager?.EnableStartup(launchMinimized);

    public static void DisableStartup() => _startupManager?.DisableStartup();
}
