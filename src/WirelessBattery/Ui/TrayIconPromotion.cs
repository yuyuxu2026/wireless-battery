using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WirelessBattery.Ui;

public static class TrayIconPromotion
{
    private const string KeyPath = @"Control Panel\NotifyIconSettings";
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2.5)
    ];

    public static void Schedule(NotifyIcon icon, Func<bool> stillRunning)
    {
        foreach (var delay in Delays)
        {
            var timer = new DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!stillRunning() || !TrayDisplaySettings.ShowInTray)
                    return;
                if (!TryPromote())
                    return;

                icon.Visible = false;
                icon.Visible = true;
            };
            timer.Start();
        }
    }

    public static bool TryPromote()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null)
                return false;

            var changed = false;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name, writable: true);
                if (sub?.GetValue("ExecutablePath") is not string exe)
                    continue;
                if (!string.Equals(exe, path, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (sub.GetValue("IsPromoted") is int promoted && promoted == 1)
                    continue;

                sub.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                changed = true;
            }

            return changed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
