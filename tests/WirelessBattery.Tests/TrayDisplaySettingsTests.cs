using WirelessBattery.Ui;

namespace WirelessBattery.Tests;

public class TrayDisplaySettingsTests
{
    [Fact]
    public void MissingValueStaysVisible() => Assert.True(TrayDisplaySettings.Interpret(null, true));

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public void StoredNumberSelectsWhetherTheIconShows(int stored, bool expected) =>
        Assert.Equal(expected, TrayDisplaySettings.Interpret(stored, true));

    [Fact]
    public void ZeroTextHidesTheIcon() => Assert.False(TrayDisplaySettings.Interpret("0", true));
}
