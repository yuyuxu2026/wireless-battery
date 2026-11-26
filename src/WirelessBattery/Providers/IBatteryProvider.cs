using WirelessBattery.Models;

namespace WirelessBattery.Providers;

public interface IBatteryProvider
{
    string Id { get; }

    TimeSpan Interval { get; }

    Task<IReadOnlyList<DeviceBattery>> ReadAsync(CancellationToken cancellationToken);
}
