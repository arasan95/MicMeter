namespace MicMeter.Services;

public sealed class MeterBallistics
{
    private readonly double _releasePerSecond;

    public MeterBallistics(double releasePerSecond = 72.0)
    {
        _releasePerSecond = releasePerSecond;
        CurrentDb = LevelMath.MinimumDb;
    }

    public double CurrentDb { get; private set; }

    public double Update(double targetDb, TimeSpan elapsed)
    {
        targetDb = Math.Clamp(targetDb, LevelMath.MinimumDb, 0.0);
        if (targetDb >= CurrentDb)
        {
            CurrentDb = targetDb;
        }
        else
        {
            CurrentDb = Math.Max(targetDb, CurrentDb - (_releasePerSecond * elapsed.TotalSeconds));
        }

        return CurrentDb;
    }

    public void Reset() => CurrentDb = LevelMath.MinimumDb;
}

