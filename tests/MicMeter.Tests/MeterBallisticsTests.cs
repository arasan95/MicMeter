using MicMeter.Services;

namespace MicMeter.Tests;

public sealed class MeterBallisticsTests
{
    [Fact]
    public void Update_AttacksImmediately()
    {
        var meter = new MeterBallistics();
        Assert.Equal(-10, meter.Update(-10, TimeSpan.FromMilliseconds(33)));
    }

    [Fact]
    public void Update_ReleasesGradually()
    {
        var meter = new MeterBallistics(60);
        meter.Update(-10, TimeSpan.Zero);
        Assert.Equal(-16, meter.Update(-40, TimeSpan.FromMilliseconds(100)), 3);
    }

    [Fact]
    public void Reset_ReturnsToFloor()
    {
        var meter = new MeterBallistics();
        meter.Update(-5, TimeSpan.Zero);
        meter.Reset();
        Assert.Equal(LevelMath.MinimumDb, meter.CurrentDb);
    }
}
