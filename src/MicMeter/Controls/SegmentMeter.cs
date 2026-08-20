using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MicMeter.Services;
using AvaloniaOrientation = Avalonia.Layout.Orientation;

namespace MicMeter.Controls;

public sealed class SegmentMeter : Control
{
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.FromRgb(35, 47, 59));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(77, 39, 49));
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.FromRgb(46, 230, 166));
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.FromRgb(255, 200, 87));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.FromRgb(255, 93, 115));
    private static readonly IPen PeakPen = new Pen(new SolidColorBrush(Color.FromRgb(125, 211, 252)), 1.5);

    private double _levelDb = LevelMath.MinimumDb;
    private int _segmentCount = 20;
    private bool _isMuted;
    private double _peakDb = LevelMath.MinimumDb;
    private AvaloniaOrientation _orientation = AvaloniaOrientation.Horizontal;
    private IBrush _lowBrush = GreenBrush;
    private IBrush _midBrush = YellowBrush;
    private IBrush _highBrush = RedBrush;
    private double _midThresholdDb = -12;
    private double _highThresholdDb = -6;

    public double LevelDb
    {
        get => _levelDb;
        set { _levelDb = value; InvalidateVisual(); }
    }

    public int SegmentCount
    {
        get => _segmentCount;
        set { _segmentCount = value; InvalidateVisual(); }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set { _isMuted = value; InvalidateVisual(); }
    }

    public double PeakDb
    {
        get => _peakDb;
        set { _peakDb = value; InvalidateVisual(); }
    }

    public AvaloniaOrientation Orientation
    {
        get => _orientation;
        set { _orientation = value; InvalidateVisual(); }
    }

    public IBrush LowLevelBrush
    {
        get => _lowBrush;
        set { _lowBrush = value; InvalidateVisual(); }
    }

    public IBrush MidLevelBrush
    {
        get => _midBrush;
        set { _midBrush = value; InvalidateVisual(); }
    }

    public IBrush HighLevelBrush
    {
        get => _highBrush;
        set { _highBrush = value; InvalidateVisual(); }
    }

    public double MidLevelThresholdDb
    {
        get => _midThresholdDb;
        set { _midThresholdDb = value; InvalidateVisual(); }
    }

    public double HighLevelThresholdDb
    {
        get => _highThresholdDb;
        set { _highThresholdDb = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var count = Math.Clamp(SegmentCount, 8, 40);
        var activeCount = (int)Math.Ceiling(LevelMath.NormalizeDb(LevelDb) * count);
        var vertical = Orientation == AvaloniaOrientation.Vertical;
        var length = vertical ? Bounds.Height : Bounds.Width;
        if (length <= 0)
        {
            return;
        }

        var gap = Math.Max(1.0, length / 120.0);
        var segmentLength = Math.Max(1.0, (length - (gap * (count - 1))) / count);

        for (var index = 0; index < count; index++)
        {
            var segmentDb = LevelMath.MinimumDb + ((index + 1.0) / count * -LevelMath.MinimumDb);
            var active = !IsMuted && index < activeCount;
            var brush = IsMuted ? MutedBrush : active ? BrushForDb(segmentDb) : InactiveBrush;
            var offset = index * (segmentLength + gap);
            var rectangle = vertical
                ? new Rect(0, Bounds.Height - offset - segmentLength, Bounds.Width, segmentLength)
                : new Rect(offset, 0, segmentLength, Bounds.Height);
            context.DrawRectangle(brush, null, new RoundedRect(rectangle, 1.2));
        }

        if (!IsMuted && PeakDb > LevelMath.MinimumDb)
        {
            var normalizedPeak = LevelMath.NormalizeDb(PeakDb);
            if (vertical)
            {
                var y = Bounds.Height * (1 - normalizedPeak);
                context.DrawLine(PeakPen, new Point(-1, y), new Point(Bounds.Width + 1, y));
            }
            else
            {
                var x = Bounds.Width * normalizedPeak;
                context.DrawLine(PeakPen, new Point(x, -1), new Point(x, Bounds.Height + 1));
            }
        }
    }

    private IBrush BrushForDb(double db) =>
        MeterBandSelector.Select(db, MidLevelThresholdDb, HighLevelThresholdDb) switch
        {
            2 => HighLevelBrush,
            1 => MidLevelBrush,
            _ => LowLevelBrush
        };
}
