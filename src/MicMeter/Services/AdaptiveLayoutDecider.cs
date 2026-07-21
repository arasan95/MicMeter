using MicMeter.Models;

namespace MicMeter.Services;

public static class AdaptiveLayoutDecider
{
    public static bool ShouldUseCompactMode(
        MeterDisplayOrientation orientation,
        double width,
        double height,
        int deviceCount)
    {
        deviceCount = Math.Max(1, deviceCount);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return false;
        }

        return orientation == MeterDisplayOrientation.Horizontal
            ? height / deviceCount < 54 || width < 220
            : width / deviceCount < 72 || height < 190;
    }
}
