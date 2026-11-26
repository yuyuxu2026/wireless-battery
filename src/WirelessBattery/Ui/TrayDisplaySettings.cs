using Microsoft.Win32;

namespace WirelessBattery.Ui;

public static class TrayDisplaySettings
{
    private const string KeyPath = @"Software\Yu\WirelessBattery";
    private const string ValueName = "ShowInTray";

    static TrayDisplaySettings() => ShowInTray = ReadStored();

    public static bool ShowInTray { get; private set; }

    public static event EventHandler? Changed;

    public static bool Interpret(object? stored, bool fallback) => stored switch
    {
        int value => value != 0,
        string text when int.TryParse(text, out var parsed) => parsed != 0,
        _ => fallback
    };

    public static void SetEnabled(bool enabled)
    {
        if (enabled == ShowInTray)
            return;

        ShowInTray = enabled;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key?.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool ReadStored()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return Interpret(key?.GetValue(ValueName), fallback: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }
}
