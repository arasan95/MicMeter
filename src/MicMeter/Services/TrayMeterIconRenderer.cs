using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MicMeter.Services;

public static class TrayMeterIconRenderer
{
    private const int CanvasSize = 32;
    private const int SegmentCount = 8;

    public static Icon Create(
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
        using var bitmap = new Bitmap(CanvasSize, CanvasSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(235, 10, 14, 18));
            graphics.FillRoundedRectangle(background, new Rectangle(2, 1, 28, 30), 5);

            if (!isConnected)
            {
                DrawDisconnected(graphics);
            }
            else if (isMuted)
            {
                DrawSegments(graphics, levelDb, lowColor, midColor, highColor,
                    midThresholdDb, highThresholdDb);
                DrawMuteSlash(graphics);
            }
            else
            {
                DrawSegments(graphics, levelDb, lowColor, midColor, highColor,
                    midThresholdDb, highThresholdDb);
            }

            if (isClipping && isConnected && !isMuted)
            {
                using var clipPen = new Pen(Color.FromArgb(255, 255, 55, 75), 2);
                graphics.DrawRoundedRectangle(clipPen, new Rectangle(2, 1, 27, 29), 5);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawMuteSlash(Graphics graphics)
    {
        using var shadowPen = new Pen(Color.FromArgb(230, 10, 14, 18), 6)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var mutePen = new Pen(Color.FromArgb(255, 255, 55, 75), 3.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(shadowPen, 7, 7, 25, 25);
        graphics.DrawLine(mutePen, 7, 7, 25, 25);
    }

    private static void DrawSegments(Graphics graphics, double levelDb, string lowColor, string midColor,
        string highColor, double midThresholdDb, double highThresholdDb)
    {
        var activeCount = (int)Math.Ceiling(LevelMath.NormalizeDb(levelDb) * SegmentCount);
        var colors = new[]
        {
            ParseColor(lowColor, Color.FromArgb(46, 230, 166)),
            ParseColor(midColor, Color.FromArgb(255, 200, 87)),
            ParseColor(highColor, Color.FromArgb(255, 93, 115))
        };
        using var inactiveBrush = new SolidBrush(Color.FromArgb(255, 47, 57, 66));
        using var lowBrush = new SolidBrush(colors[0]);
        using var midBrush = new SolidBrush(colors[1]);
        using var highBrush = new SolidBrush(colors[2]);
        var brushes = new[] { lowBrush, midBrush, highBrush };

        const int left = 7;
        const int width = 18;
        const int height = 2;
        const int gap = 1;
        const int bottom = 27;
        for (var index = 0; index < SegmentCount; index++)
        {
            var y = bottom - height - (index * (height + gap));
            var segmentDb = LevelMath.MinimumDb + ((index + 1.0) / SegmentCount * -LevelMath.MinimumDb);
            var band = MeterBandSelector.Select(segmentDb, midThresholdDb, highThresholdDb);
            var brush = index < activeCount ? brushes[band] : inactiveBrush;
            graphics.FillRectangle(brush, left, y, width, height);
        }
    }

    private static void DrawDisconnected(Graphics graphics)
    {
        using var pen = new Pen(Color.FromArgb(255, 125, 135, 145), 3)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(pen, 10, 10, 22, 22);
        graphics.DrawLine(pen, 22, 10, 10, 22);
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return ColorTranslator.FromHtml(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = CreateRoundedRectangle(bounds, radius);
        graphics.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = CreateRoundedRectangle(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
