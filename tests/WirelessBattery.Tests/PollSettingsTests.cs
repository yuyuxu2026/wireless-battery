using WirelessBattery.Polling;

namespace WirelessBattery.Tests;

public class PollSettingsTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(20, 20)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(15, 1)]
    [InlineData(90, 1)]
    public void Normalize_keeps_only_the_five_steps(int input, int expected) =>
        Assert.Equal(expected, PollSettings.Normalize(input));
}
