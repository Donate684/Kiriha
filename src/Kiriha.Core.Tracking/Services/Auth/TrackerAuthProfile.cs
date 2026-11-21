using System.Net.Http.Headers;
using System.Text.Json;
using Serilog;

namespace Kiriha.Core.Tracking.Auth;

internal static class TrackerAuthProfile
{
    public static async Task<int?> ReadUserIdAsync(
        HttpRequestMessage request, string accessToken,
        Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await send(ct);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var profile = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return profile.RootElement.ValueKind == JsonValueKind.Object &&
                profile.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt32(out var userId) && userId > 0 ? userId : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            Log.Warning(ex, "Could not resolve the tracker profile at sign-in");
            return null;
        }
    }
}
