using SkiaSharp;

namespace MicMeter.Services;

public static class TrayMeterIconRenderer
{
    private const float CanvasSize = 32;
    private const int SegmentCount = 8;

    public static byte[] CreatePng(
        double levelDb,
        bool isMuted,
        bool isConnected,
        bool isClipping,
        string lowColor,
        string midColor,
        string highColor,
        double midThresholdDb,
        double highThresholdDb)
    {
        // macOS renders at 2x (36px) so the image stays crisp on Retina; the
        // NSImage point size is set to 18pt by MacStatusIcon.
        var scale = OperatingSystem.IsMacOS() ? 36f / CanvasSize : 1f;
        var size = (int)Math.Round(CanvasSize * scale);
        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);

            // The dark rounded background helps the meter stay visible in the
            // Windows notification area, but looks out of place in the macOS
            // menu bar where colored template-style icons are the norm.
            if (!OperatingSystem.IsMacOS())
            {
                using var background = new SKPaint { Color = new SKColor(10, 14, 18, 235), IsAntialias = true };
                canvas.DrawRoundRect(new SKRect(2, 1, 30, 31), 5, 5, background);
            }

            if (!isConnected)
            {
                DrawDisconnected(canvas);
            }
            else if (isMuted)
            {
                DrawSegments(canvas, levelDb, lowColor, midColor, highColor, midThresholdDb, highThresholdDb);
                DrawMuteSlash(canvas);
            }
            else
            {
                DrawSegments(canvas, levelDb, lowColor, midColor, highColor, midThresholdDb, highThresholdDb);
            }

            if (isClipping && isConnected && !isMuted)
            {
                using var clipPen = new SKPaint
                {
                    Color = new SKColor(255, 55, 75, 255),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2
                };
                canvas.DrawRoundRect(new SKRect(2, 1, 29, 30), 5, 5, clipPen);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawMuteSlash(SKCanvas canvas)
    {
        using var shadowPen = new SKPaint
        {
            Color = new SKColor(10, 14, 18, 230),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6,
            StrokeCap = SKStrokeCap.Round
        };
        using var mutePen = new SKPaint
        {
            Color = new SKColor(255, 55, 75, 255),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3.5f,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(7, 7, 25, 25, shadowPen);
        canvas.DrawLine(7, 7, 25, 25, mutePen);
    }

    private static void DrawSegments(
        SKCanvas canvas,
        double levelDb,
        string lowColor,
        string midColor,
        string highColor,
        double midThresholdDb,
        double highThresholdDb)
    {
        var activeCount = (int)Math.Ceiling(LevelMath.NormalizeDb(levelDb) * SegmentCount);
        var colors = new[]
        {
            ParseColor(lowColor, new SKColor(46, 230, 166, 255)),
            ParseColor(midColor, new SKColor(255, 200, 87, 255)),
            ParseColor(highColor, new SKColor(255, 93, 115, 255))
        };
        var inactive = new SKColor(47, 57, 66, 255);

        const float left = 7;
        const float width = 18;
        const float height = 2;
        const float gap = 1;
        const float bottom = 27;
        for (var index = 0; index < SegmentCount; index++)
        {
            var y = bottom - height - (index * (height + gap));
            var segmentDb = LevelMath.MinimumDb + ((index + 1.0) / SegmentCount * -LevelMath.MinimumDb);
            var band = MeterBandSelector.Select(segmentDb, midThresholdDb, highThresholdDb);
            var color = index < activeCount ? colors[band] : inactive;
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawRect(new SKRect(left, y, left + width, y + height), paint);
        }
    }

    private static void DrawDisconnected(SKCanvas canvas)
    {
        using var pen = new SKPaint
        {
            Color = new SKColor(125, 135, 145, 255),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(10, 10, 22, 22, pen);
        canvas.DrawLine(22, 10, 10, 22, pen);
    }

    private static SKColor ParseColor(string value, SKColor fallback)
    {
        try
        {
            return SKColor.Parse(value);
        }
        catch
        {
            return fallback;
        }
    }
}
