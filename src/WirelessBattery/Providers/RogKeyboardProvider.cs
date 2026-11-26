using System.Management;
using WirelessBattery.Models;

namespace WirelessBattery.Providers;

public sealed class RogKeyboardProvider : IBatteryProvider
{
    public string Id => "rog";

    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public Task<IReadOnlyList<DeviceBattery>> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(Read, cancellationToken);

    private static IReadOnlyList<DeviceBattery> Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Status FROM Win32_PnPEntity WHERE Name = 'ROG FALCHION ACE HFX'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (!string.Equals(item["Status"]?.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
                        continue;

                    return
                    [
                        new DeviceBattery
                        {
                            Id = "usb:0B05:1B7E",
                            Name = "ROG FALCHION ACE HFX",
                            Source = "USB",
                            Kind = BatteryReadingKind.Unsupported,
                            StatusText = "已连接，暂不支持电量",
                            UpdatedAt = DateTimeOffset.Now
                        }
                    ];
                }
            }
        }
        catch (ManagementException)
        {
            return [];
        }

        return [];
    }
}
