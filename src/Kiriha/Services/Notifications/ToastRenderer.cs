using Serilog;

namespace Kiriha.Services.Notifications;

internal static class ToastRenderer
{
#if WINDOWS
    private const string AumId = Kiriha.Core.Domain.Constants.AppConstants.System.AppName;
#endif

    internal const int MaxToastLines = 3;

    internal static string BuildToastXml(IReadOnlyList<string> lines, string? imagePath = null)
    {
        var clamped = lines.Count > MaxToastLines ? MaxToastLines : lines.Count;

        var sb = new System.Text.StringBuilder("<toast><visual><binding template=\"ToastGeneric\">");
        for (int i = 0; i < clamped; i++)
        {
            var text = System.Security.SecurityElement.Escape(lines[i] ?? string.Empty);
            sb.Append("<text>").Append(text).Append("</text>");
        }

        var localImageUri = TryResolveLocalImageUri(imagePath);
        if (!string.IsNullOrEmpty(localImageUri))
        {
            var escapedPath = System.Security.SecurityElement.Escape(localImageUri);
            sb.Append("<image placement=\"appLogoOverride\" hint-crop=\"circle\" src=\"").Append(escapedPath).Append("\"/>");
        }

        sb.Append("</binding></visual></toast>");
        return sb.ToString();
    }

    internal static string? TryResolveLocalImageUri(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;

        if (Uri.TryCreate(imagePath, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile && File.Exists(uri.LocalPath))
            {
                return uri.AbsoluteUri;
            }
            // Unpackaged Win32 ToastGeneric ignores remote http/https image URLs.
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(imagePath);
            if (File.Exists(fullPath))
            {
                return new Uri(fullPath).AbsoluteUri;
            }
        }
        catch
        {
            // Ignore malformed paths
        }

        return null;
    }

    /// <summary>
    /// Renders a toast with up to 3 text lines and an optional circular thumbnail image.
    /// The first line is bolded by the system template; remaining lines render as regular body text.
    /// </summary>
    public static void Show(IReadOnlyList<string> lines, string? imagePath = null)
    {
        if (lines is null || lines.Count == 0) return;
        try
        {
#if WINDOWS
            var xml = BuildToastXml(lines, imagePath);
            var xmlDoc = new global::Windows.Data.Xml.Dom.XmlDocument();
            xmlDoc.LoadXml(xml);

            var toast = new global::Windows.UI.Notifications.ToastNotification(xmlDoc);
            global::Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(AumId).Show(toast);
#else
            Log.Debug("ToastRenderer: Toast not shown (non-Windows build): {Lines}", string.Join(" | ", lines));
#endif
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ToastRenderer: Failed to show toast '{First}'", lines.Count > 0 ? lines[0] : "<empty>");
        }
    }
}
