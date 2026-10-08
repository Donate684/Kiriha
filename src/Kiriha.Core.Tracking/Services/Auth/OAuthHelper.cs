using System.Net;
using System.Text;
using Kiriha.Core.Tracking.Utils;
using Kiriha.Infrastructure.Platform;
using Serilog;

namespace Kiriha.Core.Tracking.Auth;

public static class OAuthHelper
{
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromMinutes(5);

    public static async Task<string?> AuthorizeViaLoopbackAsync(
        string authUrl,
        string redirectUri,
        string successMessage,
        string? closeMessage = null,
        CancellationToken cancellationToken = default)
    {
        using var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add(redirectUri);
            listener.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start HttpListener for OAuth loopback on {RedirectUri}", redirectUri);
            return null;
        }

        Log.Information("Opening browser for authorization...");
        ShellLauncher.OpenUrl(authUrl);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(AuthTimeout);

        using var reg = linkedCts.Token.Register(() =>
        {
            try { listener.Stop(); } catch (Exception ex) { Log.Debug(ex, "Failed to stop listener on cancellation"); }
        });

        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException) when (linkedCts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (linkedCts.Token.IsCancellationRequested)
                {
                    break;
                }

                var request = context.Request;

                // Ignore favicon and other irrelevant requests
                if (request.Url?.AbsolutePath == "/favicon.ico")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.Close();
                    continue;
                }

                var code = request.QueryString["code"];
                if (string.IsNullOrEmpty(code))
                {
                    // If no code, but it's not a favicon, maybe it's an error from the provider?
                    var error = request.QueryString["error"];
                    if (!string.IsNullOrEmpty(error))
                    {
                        Log.Error("OAuth error returned from provider: {Error}", error);
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        context.Response.Close();
                        return null;
                    }

                    // Otherwise keep waiting
                    continue;
                }

                // We got the code!
                using var response = context.Response;
                response.ContentType = "text/html; charset=utf-8";
                string localizedCloseMsg = !string.IsNullOrWhiteSpace(closeMessage) ? closeMessage : TrackingLoc.GetLoc("auth.close_window");
                var responseString = $"<html><head><meta charset='utf-8'></head><body><h1 style='font-family:sans-serif;'>{successMessage}</h1><p style='font-family:sans-serif;'>{localizedCloseMsg}</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, linkedCts.Token);
                await response.OutputStream.FlushAsync(linkedCts.Token);

                // Brief delay to ensure browser receives response
                try
                {
                    await Task.Delay(500, linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation during final delay
                }

                try { listener.Stop(); } catch { }
                return code;
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Authorization timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during loopback authorization");
        }
        finally
        {
            try { listener.Stop(); } catch (Exception ex) { Log.Debug(ex, "Failed to stop HttpListener"); }
        }

        return null;
    }
}
