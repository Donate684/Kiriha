using System.Text.Json;
using Kiriha.Core.Domain.Models.Api;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class ShikiApiService
{
    private async Task<int?> GetCurrentUserIdAsync(CancellationToken ct)
    {
        try
        {
            using var response = await GetAsync(ShikiEndpoints.CurrentUserPath, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("id", out var idProp) &&
                idProp.TryGetInt32(out var id))
            {
                return id;
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "ShikiApiService ({Tracker}): failed to parse whoami response", Name);
            return null;
        }
    }
}
