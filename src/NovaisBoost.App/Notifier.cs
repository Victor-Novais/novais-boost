using System.Security;
using Microsoft.Win32;
using NovaisBoost.Core;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace NovaisBoost.App;

/// <summary>
/// Windows toast notifications (B8) for an unpackaged app: the AppUserModelID is registered under
/// HKCU\Software\Classes\AppUserModelId so Windows shows "NOVAIS boost" as the sender. A failure only goes to the log.
/// </summary>
internal static class Notifier
{
    private const string AppId = "NovaisBoost.App";

    public static void Show(string title, string message, IActionLog log)
    {
        try
        {
            Register();
            var xml = new XmlDocument();
            xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text>" +
                $"<text>{SecurityElement.Escape(message)}</text></binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            log.Warning($"Notificação não exibida ({error.Message}): {title} — {message}");
        }
    }

    private static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
        if (key.GetValue("DisplayName") as string != "NOVAIS boost")
            key.SetValue("DisplayName", "NOVAIS boost");
        if (Environment.ProcessPath is { } exe && !string.Equals(key.GetValue("IconUri") as string, exe, StringComparison.OrdinalIgnoreCase))
            key.SetValue("IconUri", exe);
    }
}
