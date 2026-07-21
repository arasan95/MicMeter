namespace MicMeter.Services;

public sealed class PeakHoldTracker
{
    private readonly TimeSpan _holdDuration;
    private readonly double _fallDbPerSecond;
    private TimeSpan _holdRemaining;

    public PeakHoldTracker(TimeSpan? holdDuration = null, double fallDbPerSecond = 30)
    {
        _holdDuration = holdDuration ?? TimeSpan.FromMilliseconds(800);
        _fallDbPerSecond = fallDbPerSecond;
        PeakDb = LevelMath.MinimumDb;
    }

    public double PeakDb { get; private set; }

    public double Update(double levelDb, TimeSpan elapsed)
    {
        if (levelDb >= PeakDb)
        {
            PeakDb = levelDb;
            _holdRemaining = _holdDuration;
            return PeakDb;
        }

        if (_holdRemaining > TimeSpan.Zero)
        {
            _holdRemaining -= elapsed;
        }
        else
        {
            PeakDb = Math.Max(levelDb, PeakDb - (_fallDbPerSecond * elapsed.TotalSeconds));
        }

        return PeakDb;
    }

    public void Reset()
    {
        PeakDb = LevelMath.MinimumDb;
        _holdRemaining = TimeSpan.Zero;
    }
}

