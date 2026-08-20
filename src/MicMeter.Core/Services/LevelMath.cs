namespace MicMeter.Services;

public static class LevelMath
{
    public const double MinimumDb = -60.0;

    public static double PeakToDb(double peak)
    {
        if (!double.IsFinite(peak) || peak <= 0)
        {
            return MinimumDb;
        }

        return Math.Clamp(20.0 * Math.Log10(peak), MinimumDb, 0.0);
    }

    public static double NormalizeDb(double db) =>
        Math.Clamp((db - MinimumDb) / -MinimumDb, 0.0, 1.0);
}

