using System;

namespace Kiriha.Core.Services;

[Flags]
public enum SettingsSection
{
    None = 0,
    UI = 1 << 0,
    System = 1 << 1,
    Player = 1 << 2,
    Torrents = 1 << 3,
    Api = 1 << 4,
    CustomLinks = 1 << 5,
    All = UI | System | Player | Torrents | Api | CustomLinks
}