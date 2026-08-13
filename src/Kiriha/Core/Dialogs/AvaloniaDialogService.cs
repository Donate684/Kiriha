using Kiriha.Models.Entities;
using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Core.Tracking.Integration;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Core;
using Kiriha.Core.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Kiriha.Models;
using Kiriha.Core.Tracking.Api;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;
using Kiriha.ViewModels;
using Kiriha.ViewModels.AnimeDetails;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Core.Dialogs;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>. Owns the rules for
/// picking an owner window, deferring dialog opening until the main window is
/// visible (so a dialog doesn't materialise behind a hidden / minimised root),
/// and resolving dependencies for the dialog ViewModels via DI rather than the
/// static <c>App.GetService</c> locator.
/// </summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Kiriha.Core.Services.ISettingsService _settingsService; private readonly Kiriha.Core.Services.ISyncManager _syncManager; private readonly Kiriha.Core.Repositories.IAnimeRepository _animeRepo; private readonly Kiriha.Core.Services.IProgressUpdateService _progressService; private readonly Kiriha.Core.Services.IHistoryService _historyService; private readonly Kiriha.Core.Services.IMalApiService _malApiService; private readonly Kiriha.Core.Services.IShikiApiService _shikiApiService; private readonly Kiriha.Core.Tracking.Api.JikanApiService _jikanApiService;

    public AvaloniaDialogService(Kiriha.Core.Services.ISettingsService settingsService, Kiriha.Core.Services.ISyncManager syncManager, Kiriha.Core.Repositories.IAnimeRepository animeRepo, Kiriha.Core.Services.IProgressUpdateService progressService, Kiriha.Core.Services.IHistoryService historyService, Kiriha.Core.Services.IMalApiService malApiService, Kiriha.Core.Services.IShikiApiService shikiApiService, Kiriha.Core.Tracking.Api.JikanApiService jikanApiService)
    {
        _settingsService = settingsService; _syncManager = syncManager; _animeRepo = animeRepo; _progressService = progressService; _historyService = historyService; _malApiService = malApiService; _shikiApiService = shikiApiService; _jikanApiService = jikanApiService;
    }

    public async Task<bool> ShowAnimeDetailsAsync(Control? sourceControl, AnimeEntity item, CancellationToken ct = default)
    {
        var owner = ResolveOwner(sourceControl);
        if (owner == null) return false;

        // Resolve dependencies via DI scope. Note the dialog's VM is currently
        // not registered in the container (it carries a load of per-call state),
        // so we new it up explicitly with services pulled from the provider.
        var clone = item.Clone();

        var editVm = new AnimeEditViewModel(
            item,
            clone,
            _syncManager,
            _animeRepo,
            _progressService,
            _historyService);
            
        var metaVm = new AnimeMetadataViewModel(
            clone,
            _malApiService,
            _shikiApiService);

        var vm = new AnimeDetailsViewModel(
            clone,
            editVm,
            metaVm,
            _jikanApiService,
            _settingsService,
            this,
            _shikiApiService,
            _animeRepo,
            _malApiService);

        var window = new Views.AnimeDetailsWindow(_settingsService) { DataContext = vm };

        try
        {
            await WaitForVisibleAsync(owner, ct);
            var result = await window.ShowDialog<bool?>(owner);
            return result == true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task ShowUpdateDialogAsync(bool isDownloaded = false, CancellationToken ct = default)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        try
        {
            await WaitForVisibleAsync(desktop.MainWindow, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (desktop.MainWindow.DataContext is MainWindowViewModel mainVm)
            mainVm.ShowUpdateDialog(isDownloaded);
    }

    /// <summary>
    /// Picks the window that should own a dialog: prefer the top-level of the
    /// triggering control (so the dialog is centred over the actual surface
    /// the user clicked on), fall back to the desktop main window.
    /// </summary>
    private static Window? ResolveOwner(Control? source)
    {
        if (source != null && TopLevel.GetTopLevel(source) is Window w) return w;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    /// <summary>
    /// Suspends until <paramref name="window"/> is both visible and not minimised.
    /// Throws <see cref="OperationCanceledException"/> if the window is closed
    /// before becoming visible, or if <paramref name="ct"/> fires.
    /// Implemented with strongly-typed property comparison (no string lookup).
    /// </summary>
    private static Task WaitForVisibleAsync(Window window, CancellationToken ct)
    {
        if (window.IsVisible && window.WindowState != WindowState.Minimized)
            return Task.CompletedTask;

        Log.Information("DialogService: main window is hidden/minimised, deferring dialog until visible");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<AvaloniaPropertyChangedEventArgs>? propertyHandler = null;
        EventHandler<WindowClosingEventArgs>? closingHandler = null;
        CancellationTokenRegistration ctRegistration = default;

        void Cleanup()
        {
            if (propertyHandler != null) window.PropertyChanged -= propertyHandler;
            if (closingHandler != null) window.Closing -= closingHandler;
            ctRegistration.Dispose();
        }

        propertyHandler = (_, args) =>
        {
            // Strongly-typed compare: avoids the reflection-y string lookup that
            // the old UIUtils used and survives Avalonia property renames.
            if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty)
            {
                if (window.IsVisible && window.WindowState != WindowState.Minimized)
                {
                    Cleanup();
                    tcs.TrySetResult(true);
                }
            }
        };
        closingHandler = (_, _) =>
        {
            Cleanup();
            tcs.TrySetException(new OperationCanceledException("Main window closed before becoming visible"));
        };

        window.PropertyChanged += propertyHandler;
        window.Closing += closingHandler;

        if (ct.CanBeCanceled)
        {
            ctRegistration = ct.Register(() =>
            {
                Cleanup();
                tcs.TrySetCanceled(ct);
            });
        }

        return tcs.Task;
    }
}
