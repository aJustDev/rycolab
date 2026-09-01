using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Rycolab.Core;

/// <summary>
/// Windows toast plus chime for the guard's bad news. The toast goes out
/// silent and the wav next to the exe plays through winmm: unpackaged apps
/// only get Windows' stock ms-winsoundevent sounds on the toast itself.
/// </summary>
public static class Notifier
{
    private const string AppId = "rycolab";
    private static bool _registered;

    /// <summary>Never throws: a notification must not take the guard down.</summary>
    public static bool Notify(string title, string body)
    {
        try
        {
            EnsureAppId();
            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast><visual><binding template="ToastGeneric">
                <text>{SecurityElement.Escape(title)}</text>
                <text>{SecurityElement.Escape(body)}</text>
                </binding></visual><audio silent="true"/></toast>
                """);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
            var wav = Path.Combine(AppContext.BaseDirectory, "rycolab-alert.wav");
            if (File.Exists(wav)) PlaySound(wav, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>An unpackaged exe needs its AppUserModelID in the registry for toasts to show.</summary>
    private static void EnsureAppId()
    {
        if (_registered) return;
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
        key.SetValue("DisplayName", "rycolab");
        _registered = true;
    }

    private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_FILENAME = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string sound, IntPtr hmod, uint flags);
}
