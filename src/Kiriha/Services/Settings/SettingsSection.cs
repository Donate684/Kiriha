using Kiriha.Services.Data.Settings;
using System;
using System.Text.Json.Serialization;
using Kiriha.Models;

namespace Kiriha.Services.Data.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

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
