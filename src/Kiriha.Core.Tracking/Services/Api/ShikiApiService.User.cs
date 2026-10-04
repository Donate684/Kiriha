using System.Text.Json;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class ShikiApiService
{
    private async Task<int?> GetCurrentUserIdAsync(CancellationToken ct)
    {
        try
        {
            var response = await GetAsync("users/whoami", ct);
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
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService ({Tracker}): failed to parse whoami response", Name);
            return null;
        }
    }
}
