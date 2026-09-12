using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Models;
using Kiriha.Models;

namespace Kiriha.Services.Data.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(EpisodeAiringSource))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
