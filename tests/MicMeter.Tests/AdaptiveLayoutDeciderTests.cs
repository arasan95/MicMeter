using MicMeter.Models;
using MicMeter.Services;

namespace MicMeter.Tests;

public sealed class AdaptiveLayoutDeciderTests
{
    [Theory]
    [InlineData(400, 72, 1, false)]
    [InlineData(400, 40, 1, true)]
    [InlineData(400, 80, 2, true)]
    [InlineData(180, 72, 1, true)]
    public void HorizontalMode_UsesAvailableSize(double width, double height, int devices, bool expected)
    {
        Assert.Equal(expected, AdaptiveLayoutDecider.ShouldUseCompactMode(
            MeterDisplayOrientation.Horizontal, width, height, devices));
    }

    [Theory]
    [InlineData(120, 260, 1, false)]
    [InlineData(60, 260, 1, true)]
    [InlineData(120, 160, 1, true)]
    public void VerticalMode_UsesAvailableSize(double width, double height, int devices, bool expected)
    {
        Assert.Equal(expected, AdaptiveLayoutDecider.ShouldUseCompactMode(
            MeterDisplayOrientation.Vertical, width, height, devices));
    }
}
