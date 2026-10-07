using Serilog;

namespace Kiriha.Services.Notifications;

internal static class ToastRenderer
{
#if WINDOWS
    private const string AumId = Kiriha.Core.Domain.Constants.AppConstants.System.AppName;
#endif

    /// <summary>
    /// Renders a toast with up to 3 text lines. The first line is bolded by the
    /// system template; remaining lines render as regular body text.
    /// </summary>
    public static void Show(IReadOnlyList<string> lines)
    {
        if (lines is null || lines.Count == 0) return;
        try
        {
#if WINDOWS
            var clamped = lines.Count > 3 ? 3 : lines.Count;

            var sb = new System.Text.StringBuilder("<toast><visual><binding template=\"ToastGeneric\">");
            for (int i = 0; i < clamped; i++)
            {
                var text = System.Security.SecurityElement.Escape(lines[i] ?? string.Empty);
                sb.Append("<text>").Append(text).Append("</text>");
            }
            sb.Append("</binding></visual></toast>");

            var xmlDoc = new global::Windows.Data.Xml.Dom.XmlDocument();
            xmlDoc.LoadXml(sb.ToString());

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
