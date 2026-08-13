using System;

using Kiriha.Core.Abstractions.Models;

namespace Kiriha.Core.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Update(Action<AppSettings> update, bool save = true);
    void Update(Action<AppSettings> update, SettingsSection changedSections, bool save = true);
    bool NeedsFirstStartup();
    void CompleteSetupStep(string key);
    void Save();
    System.Threading.Tasks.Task SaveAsync();
    T Read<T>(Func<AppSettings, T> read);
    void SaveImmediate();
}
