using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Services.Data.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppConfigFile))]
[JsonSerializable(typeof(AppSettings.PlayerConfig))]
[JsonSerializable(typeof(AppSettings.TorrentConfig))]
[JsonSerializable(typeof(AppSettings.ApiConfig))]
[JsonSerializable(typeof(AppSettings.WindowPlacement))]
[JsonSerializable(typeof(EpisodeAiringSource))]
[JsonSerializable(typeof(TrackerAccount))]
[JsonSerializable(typeof(List<TrackerAccount>))]
[JsonSerializable(typeof(OAuthTokens))]
[JsonSerializable(typeof(MalTokens))]
[JsonSerializable(typeof(ShikiTokens))]
[JsonSerializable(typeof(AniListTokens))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
