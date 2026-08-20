namespace MicMeter.Services;

public static class MeterBandSelector
{
    public static int Select(double db, double midThresholdDb, double highThresholdDb)
    {
        if (db >= highThresholdDb) return 2;
        if (db >= midThresholdDb) return 1;
        return 0;
    }
}
