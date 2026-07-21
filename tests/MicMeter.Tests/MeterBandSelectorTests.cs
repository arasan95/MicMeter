using MicMeter.Services;

namespace MicMeter.Tests;

public sealed class MeterBandSelectorTests
{
    [Theory]
    [InlineData(-60, -12, -6, 0)]
    [InlineData(-12.01, -12, -6, 0)]
    [InlineData(-12, -12, -6, 1)]
    [InlineData(-6.01, -12, -6, 1)]
    [InlineData(-6, -12, -6, 2)]
    [InlineData(0, -12, -6, 2)]
    public void Select_UsesConfiguredBoundaries(double db, double mid, double high, int expected)
    {
        Assert.Equal(expected, MeterBandSelector.Select(db, mid, high));
    }
}
