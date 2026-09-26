using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Models;
using Kiriha.Models;

namespace Kiriha.Services.Data.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppConfigFile))]
[JsonSerializable(typeof(AppSettings.PlayerConfig))]
[JsonSerializable(typeof(AppSettings.TorrentConfig))]
[JsonSerializable(typeof(AppSettings.ApiConfig))]
[JsonSerializable(typeof(AppSettings.WindowPlacement))]
[JsonSerializable(typeof(EpisodeAiringSource))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
