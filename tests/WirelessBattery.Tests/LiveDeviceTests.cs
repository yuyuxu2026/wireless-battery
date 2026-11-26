using WirelessBattery.Models;
using WirelessBattery.Providers;

namespace WirelessBattery.Tests;

public class LiveDeviceTests
{
    [Fact]
    public async Task LaptopReportsAPercentage()
    {
        var devices = await new LaptopBatteryProvider().ReadAsync(CancellationToken.None);
        var laptop = Assert.Single(devices);
        Assert.Equal("笔记本电池", laptop.Name);
        Assert.Equal(BatteryReadingKind.Level, laptop.Kind);
        Assert.InRange(laptop.Percent!.Value, 0, 100);
    }

    [Fact]
    public async Task LogitechMouseReportsNameAndPercentage()
    {
        var devices = await new LogitechHidppProvider().ReadAsync(CancellationToken.None);
        var mouse = Assert.Single(devices);
        Assert.Contains("PRO", mouse.Name);
        Assert.Equal(BatteryReadingKind.Level, mouse.Kind);
        Assert.NotNull(mouse.Percent);
        Assert.InRange(mouse.Percent.Value, 0, 100);
        Assert.False(mouse.IsStale);
    }

    [Fact]
    public async Task BluetoothReadDoesNotThrow()
    {
        var devices = await new BleBatteryProvider().ReadAsync(CancellationToken.None);
        Assert.DoesNotContain(devices, device => device.StatusText.Contains("拒绝", StringComparison.Ordinal));
        foreach (var device in devices)
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Name));
            if (device.Kind == BatteryReadingKind.Level)
            {
                Assert.NotNull(device.Percent);
                Assert.InRange(device.Percent.Value, 0, 100);
            }
            else
            {
                Assert.Null(device.Percent);
                Assert.False(string.IsNullOrWhiteSpace(device.StatusText));
            }
        }
    }
}
