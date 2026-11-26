namespace WirelessBattery.Models;

public sealed record LowBatteryAlert(string DeviceId, string DeviceName, int Percent, int Threshold);
