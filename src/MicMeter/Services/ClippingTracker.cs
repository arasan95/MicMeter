namespace MicMeter.Services;

public sealed class ClippingTracker
{
    private readonly double _thresholdDb;
    private readonly TimeSpan _warningDuration;
    private TimeSpan _remaining;

    public ClippingTracker(double thresholdDb = -0.5, TimeSpan? warningDuration = null)
    {
        _thresholdDb = thresholdDb;
        _warningDuration = warningDuration ?? TimeSpan.FromSeconds(2);
    }

    public bool Update(double levelDb, TimeSpan elapsed)
    {
        if (levelDb >= _thresholdDb)
        {
            _remaining = _warningDuration;
        }
        else
        {
            _remaining = _remaining > elapsed ? _remaining - elapsed : TimeSpan.Zero;
        }

        return _remaining > TimeSpan.Zero;
    }

    public void Reset() => _remaining = TimeSpan.Zero;
}
