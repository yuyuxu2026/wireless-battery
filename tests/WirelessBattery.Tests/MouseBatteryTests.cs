using WirelessBattery.Models;

namespace WirelessBattery.Tests;

public class MouseBatteryTests
{
    [Fact]
    public void SelectsTheLogitechMouseInsteadOfTheLaptop()
    {
        var mouse = MouseBattery.Select(
        [
            Device("笔记本", 100, "笔记本电池"),
            Device("罗技接收器", 15, "PRO X 2")
        ]);

        Assert.NotNull(mouse);
        Assert.Equal("PRO X 2", mouse.Name);
        Assert.Equal(15, mouse.Percent);
    }

    [Fact]
    public void PrefersAFreshReadingOverAStaleOne()
    {
        var mouse = MouseBattery.Select(
        [
            Device("罗技接收器", 10, "旧鼠标", stale: true),
            Device("罗技接收器", 40, "PRO X 2")
        ]);

        Assert.Equal("PRO X 2", mouse?.Name);
    }

    [Fact]
    public void ReturnsNullWhenTheMouseHasNoPercentage()
    {
        Assert.Null(MouseBattery.Select([Device("笔记本", 80, "笔记本电池")]));
    }

    private static DeviceBattery Device(string source, int percent, string name, bool stale = false) => new()
    {
        Id = source + name,
        Name = name,
        Source = source,
        Kind = BatteryReadingKind.Level,
        Percent = percent,
        IsStale = stale
    };
}
