using MicMeter.Services;

namespace MicMeter.Tests;

public sealed class PeakHoldTrackerTests
{
    [Fact]
    public void Update_HoldsPeakForConfiguredDuration()
    {
        var tracker = new PeakHoldTracker(TimeSpan.FromMilliseconds(800), 30);
        tracker.Update(-3, TimeSpan.Zero);
        Assert.Equal(-3, tracker.Update(-20, TimeSpan.FromMilliseconds(500)), 3);
    }

    [Fact]
    public void Update_FallsAfterHoldExpires()
    {
        var tracker = new PeakHoldTracker(TimeSpan.FromMilliseconds(500), 30);
        tracker.Update(-3, TimeSpan.Zero);
        tracker.Update(-20, TimeSpan.FromMilliseconds(600));
        Assert.Equal(-6, tracker.Update(-20, TimeSpan.FromMilliseconds(100)), 3);
    }

    [Fact]
    public void Update_ImmediatelyAcceptsHigherPeak()
    {
        var tracker = new PeakHoldTracker();
        tracker.Update(-20, TimeSpan.Zero);
        Assert.Equal(-2, tracker.Update(-2, TimeSpan.FromMilliseconds(10)), 3);
    }
}

