namespace WirelessBattery.Models;

public static class MouseBattery
{
    public static DeviceBattery? Select(IEnumerable<DeviceBattery> devices) =>
        devices
            .Where(device => device.Source == "罗技接收器" && device.Percent is not null)
            .OrderBy(device => device.IsStale)
            .ThenBy(device => device.Percent)
            .FirstOrDefault();
}
