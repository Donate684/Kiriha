using System.Text.Json.Serialization;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Services.Data.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
