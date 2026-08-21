using System;
using System.Collections.Generic;
using Serilog;

namespace Kiriha.Services.Notifications;

internal static class ToastRenderer
{
    private const string AumId = Kiriha.Core.Domain.Constants.AppConstants.System.AppName;

    /// <summary>
    /// Renders a toast with up to 3 text lines. The first line is bolded by the
    /// system template; remaining lines render as regular body text.
    /// </summary>
    public static void Show(IReadOnlyList<string> lines)
    {
        if (lines == null || lines.Count == 0) return;
        try
        {
#if WINDOWS
            var clamped = lines.Count > 3 ? 3 : lines.Count;

            var xmlString = "<toast><visual><binding template=\"ToastGeneric\">";
            for (int i = 0; i < clamped; i++)
            {
                var text = (lines[i] ?? string.Empty)
                    .Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
                xmlString += $"<text>{text}</text>";
            }
            xmlString += "</binding></visual></toast>";

            var xmlDoc = new global::Windows.Data.Xml.Dom.XmlDocument();
            xmlDoc.LoadXml(xmlString);

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
