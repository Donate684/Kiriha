using System;
using Kiriha.Models;

namespace Kiriha.Core.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Update(Action<AppSettings> update, bool save = true);
    T Read<T>(Func<AppSettings, T> read);
    void SaveImmediate();
}
