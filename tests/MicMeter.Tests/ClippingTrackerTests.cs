using MicMeter.Services;

namespace MicMeter.Tests;

public sealed class ClippingTrackerTests
{
    [Fact]
    public void Update_TriggersAtThreshold()
    {
        var tracker = new ClippingTracker(-0.5, TimeSpan.FromSeconds(2));
        Assert.True(tracker.Update(-0.5, TimeSpan.Zero));
    }

    [Fact]
    public void Update_LatchesWarningThenClears()
    {
        var tracker = new ClippingTracker(-0.5, TimeSpan.FromSeconds(2));
        tracker.Update(-0.1, TimeSpan.Zero);
        Assert.True(tracker.Update(-20, TimeSpan.FromSeconds(1)));
        Assert.False(tracker.Update(-20, TimeSpan.FromSeconds(1.1)));
    }
}
