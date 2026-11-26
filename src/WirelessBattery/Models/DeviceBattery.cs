namespace WirelessBattery.Models;

public enum BatteryReadingKind
{
    Level,
    Unsupported,
    Unavailable
}

public sealed class DeviceBattery
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Source { get; init; }

    public BatteryReadingKind Kind { get; init; }

    public int? Percent { get; init; }

    public bool? IsCharging { get; init; }

    public string StatusText { get; init; } = "";

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public bool IsStale { get; init; }

    public bool CountsForTray =>
        Kind == BatteryReadingKind.Level &&
        Percent is not null &&
        !IsStale &&
        Source != "笔记本";
}
