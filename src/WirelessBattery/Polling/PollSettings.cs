using Microsoft.Win32;

namespace WirelessBattery.Polling;

public static class PollSettings
{
    public static readonly int[] Choices = [1, 10, 20, 30, 60];

    private const string KeyPath = @"Software\Yu\WirelessBattery";
    private const string ValueName = "PollMinutes";

    static PollSettings() => Minutes = ReadStored();

    public static int Minutes { get; private set; }

    public static TimeSpan Interval => TimeSpan.FromMinutes(Minutes);

    public static event EventHandler? Changed;

    public static string Label(int minutes) => $"{minutes} 分钟";

    public static int Normalize(int minutes) => Choices.Contains(minutes) ? minutes : Choices[0];

    public static void SetMinutes(int minutes)
    {
        minutes = Normalize(minutes);
        if (minutes == Minutes)
            return;

        Minutes = minutes;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key?.SetValue(ValueName, minutes, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static int ReadStored()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue(ValueName) switch
            {
                int value => Normalize(value),
                string text when int.TryParse(text, out var parsed) => Normalize(parsed),
                _ => Choices[0]
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Choices[0];
        }
    }
}
