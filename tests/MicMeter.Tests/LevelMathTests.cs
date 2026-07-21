using MicMeter.Services;

namespace MicMeter.Tests;

public sealed class LevelMathTests
{
    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.5, -6.0206)]
    [InlineData(0.1, -20.0)]
    [InlineData(0.001, -60.0)]
    [InlineData(0.0, -60.0)]
    [InlineData(-1.0, -60.0)]
    public void PeakToDb_ReturnsExpectedLevel(double peak, double expected)
    {
        Assert.Equal(expected, LevelMath.PeakToDb(peak), 3);
    }

    [Theory]
    [InlineData(-60.0, 0.0)]
    [InlineData(-30.0, 0.5)]
    [InlineData(0.0, 1.0)]
    public void NormalizeDb_ReturnsUnitRange(double db, double expected)
    {
        Assert.Equal(expected, LevelMath.NormalizeDb(db), 3);
    }
}

