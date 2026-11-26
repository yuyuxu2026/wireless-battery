using System.Management;
using WirelessBattery.Models;

namespace WirelessBattery.Providers;

public sealed class LaptopBatteryProvider : IBatteryProvider
{
    public string Id => "laptop";

    public Task<IReadOnlyList<DeviceBattery>> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(Read, cancellationToken);

    private static IReadOnlyList<DeviceBattery> Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["EstimatedChargeRemaining"] is null)
                        continue;

                    var percent = Convert.ToInt32(item["EstimatedChargeRemaining"]);
                    var status = item["BatteryStatus"] is null ? 0 : Convert.ToInt32(item["BatteryStatus"]);
                    var charging = status is 6 or 7 or 8 or 9;
                    var full = status == 3 || percent >= 100;
                    return
                    [
                        new DeviceBattery
                        {
                            Id = "laptop",
                            Name = "笔记本电池",
                            Source = "笔记本",
                            Kind = BatteryReadingKind.Level,
                            Percent = Math.Clamp(percent, 0, 100),
                            IsCharging = charging,
                            StatusText = charging ? "充电中" : full ? "已充满" : "使用中",
                            UpdatedAt = DateTimeOffset.Now
                        }
                    ];
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            return
            [
                Unavailable("读取笔记本电量失败")
            ];
        }

        return [];
    }

    private static DeviceBattery Unavailable(string status) => new()
    {
        Id = "laptop",
        Name = "笔记本电池",
        Source = "笔记本",
        Kind = BatteryReadingKind.Unavailable,
        StatusText = status,
        UpdatedAt = DateTimeOffset.Now
    };
}
