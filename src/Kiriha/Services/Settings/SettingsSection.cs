using System.Text.Json.Serialization;
using Kiriha.Models;

namespace Kiriha.Services.Data.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
