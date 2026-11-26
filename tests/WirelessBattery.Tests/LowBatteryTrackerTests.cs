using WirelessBattery.Models;
using WirelessBattery.Polling;

namespace WirelessBattery.Tests;

public class LowBatteryTrackerTests
{
    [Fact]
    public void WarnsOnceUntilTheDeviceClimbsBackAboveTheThreshold()
    {
        var tracker = new LowBatteryTracker(warnPercent: 20, criticalPercent: 10);
        var low = Device(15);

        var first = tracker.Observe([low]);
        var second = tracker.Observe([low]);

        var alert = Assert.Single(first);
        Assert.Equal(20, alert.Threshold);
        Assert.Empty(second);
    }

    [Fact]
    public void RaisedThresholdStillFiresOnlyOnce()
    {
        var tracker = new LowBatteryTracker(warnPercent: 50, criticalPercent: 10);
        var device = Device(15);

        Assert.Single(tracker.Observe([device]));
        Assert.Empty(tracker.Observe([device]));
    }

    [Fact]
    public void CriticalAlertFiresAfterTheWarningAndRearmsAboveThatLevel()
    {
        var tracker = new LowBatteryTracker();
        Assert.Equal(20, Assert.Single(tracker.Observe([Device(15)])).Threshold);

        var critical = Assert.Single(tracker.Observe([Device(8)]));
        Assert.Equal(10, critical.Threshold);
        Assert.Empty(tracker.Observe([Device(8)]));

        Assert.Empty(tracker.Observe([Device(12)]));
        Assert.Equal(10, Assert.Single(tracker.Observe([Device(8)])).Threshold);
    }

    [Fact]
    public void JumpingBelowBothThresholdsSendsOnlyTheLowerAlert()
    {
        var tracker = new LowBatteryTracker();
        var alert = Assert.Single(tracker.Observe([Device(5)]));
        Assert.Equal(10, alert.Threshold);
        Assert.Empty(tracker.Observe([Device(5)]));
    }

    [Fact]
    public void ChargingAboveTheWarningRearmsIt()
    {
        var tracker = new LowBatteryTracker();
        Assert.Single(tracker.Observe([Device(15)]));
        Assert.Empty(tracker.Observe([Device(40)]));
        Assert.Equal(20, Assert.Single(tracker.Observe([Device(15)])).Threshold);
    }

    [Fact]
    public void StaleAndUnsupportedDevicesDoNotAlert()
    {
        var tracker = new LowBatteryTracker(warnPercent: 50, criticalPercent: 10);
        var stale = Device(15);
        stale = new DeviceBattery
        {
            Id = stale.Id,
            Name = stale.Name,
            Source = stale.Source,
            Kind = BatteryReadingKind.Level,
            Percent = 15,
            IsStale = true,
            StatusText = "电量已过期"
        };
        var unsupported = new DeviceBattery
        {
            Id = "bt:headphones",
            Name = "耳机",
            Source = "蓝牙",
            Kind = BatteryReadingKind.Unsupported,
            StatusText = "已连接，系统未提供电量"
        };

        Assert.Empty(tracker.Observe([stale, unsupported]));
    }

    private static DeviceBattery Device(int percent) => new()
    {
        Id = "mouse",
        Name = "PRO X 2",
        Source = "罗技接收器",
        Kind = BatteryReadingKind.Level,
        Percent = percent,
        StatusText = "使用中"
    };
}
