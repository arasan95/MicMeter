using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MicMeter.Services;
using WpfOrientation = System.Windows.Controls.Orientation;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;

namespace MicMeter.Controls;

public sealed class SegmentMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelDbProperty = DependencyProperty.Register(
        nameof(LevelDb), typeof(double), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(LevelMath.MinimumDb, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentCountProperty = DependencyProperty.Register(
        nameof(SegmentCount), typeof(int), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(
        nameof(IsMuted), typeof(bool), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PeakDbProperty = DependencyProperty.Register(
        nameof(PeakDb), typeof(double), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(LevelMath.MinimumDb, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(WpfOrientation), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(WpfOrientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowLevelBrushProperty = DependencyProperty.Register(
        nameof(LowLevelBrush), typeof(System.Windows.Media.Brush), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 230, 166)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MidLevelBrushProperty = DependencyProperty.Register(
        nameof(MidLevelBrush), typeof(System.Windows.Media.Brush), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 200, 87)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighLevelBrushProperty = DependencyProperty.Register(
        nameof(HighLevelBrush), typeof(System.Windows.Media.Brush), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 93, 115)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MidLevelThresholdDbProperty = DependencyProperty.Register(
        nameof(MidLevelThresholdDb), typeof(double), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(-12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighLevelThresholdDbProperty = DependencyProperty.Register(
        nameof(HighLevelThresholdDb), typeof(double), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(-6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly System.Windows.Media.Brush InactiveBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 47, 59)));
    private static readonly System.Windows.Media.Brush MutedBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(77, 39, 49)));
    private static readonly System.Windows.Media.Brush GreenBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 230, 166)));
    private static readonly System.Windows.Media.Brush YellowBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 200, 87)));
    private static readonly System.Windows.Media.Brush RedBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 93, 115)));
    private static readonly System.Windows.Media.Pen PeakPen = Freeze(new System.Windows.Media.Pen(
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 211, 252)), 1.5));

    public double LevelDb
    {
        get => (double)GetValue(LevelDbProperty);
        set => SetValue(LevelDbProperty, value);
    }

    public int SegmentCount
    {
        get => (int)GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    public double PeakDb
    {
        get => (double)GetValue(PeakDbProperty);
        set => SetValue(PeakDbProperty, value);
    }

    public WpfOrientation Orientation
    {
        get => (WpfOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public System.Windows.Media.Brush LowLevelBrush
    {
        get => (System.Windows.Media.Brush)GetValue(LowLevelBrushProperty);
        set => SetValue(LowLevelBrushProperty, value);
    }

    public System.Windows.Media.Brush MidLevelBrush
    {
        get => (System.Windows.Media.Brush)GetValue(MidLevelBrushProperty);
        set => SetValue(MidLevelBrushProperty, value);
    }

    public System.Windows.Media.Brush HighLevelBrush
    {
        get => (System.Windows.Media.Brush)GetValue(HighLevelBrushProperty);
        set => SetValue(HighLevelBrushProperty, value);
    }

    public double MidLevelThresholdDb
    {
        get => (double)GetValue(MidLevelThresholdDbProperty);
        set => SetValue(MidLevelThresholdDbProperty, value);
    }

    public double HighLevelThresholdDb
    {
        get => (double)GetValue(HighLevelThresholdDbProperty);
        set => SetValue(HighLevelThresholdDbProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var count = Math.Clamp(SegmentCount, 8, 40);
        var activeCount = (int)Math.Ceiling(LevelMath.NormalizeDb(LevelDb) * count);
        var vertical = Orientation == WpfOrientation.Vertical;
        var length = vertical ? ActualHeight : ActualWidth;
        var gap = Math.Max(1.0, length / 120.0);
        var segmentLength = Math.Max(1.0, (length - (gap * (count - 1))) / count);

        for (var index = 0; index < count; index++)
        {
            var segmentDb = LevelMath.MinimumDb + ((index + 1.0) / count * -LevelMath.MinimumDb);
            var active = !IsMuted && index < activeCount;
            var brush = IsMuted ? MutedBrush : active ? BrushForDb(segmentDb) : InactiveBrush;
            var offset = index * (segmentLength + gap);
            var rectangle = vertical
                ? new Rect(0, ActualHeight - offset - segmentLength, ActualWidth, segmentLength)
                : new Rect(offset, 0, segmentLength, ActualHeight);
            drawingContext.DrawRoundedRectangle(brush, null, rectangle, 1.2, 1.2);
        }


        if (!IsMuted && PeakDb > LevelMath.MinimumDb)
        {
            var normalizedPeak = LevelMath.NormalizeDb(PeakDb);
            if (vertical)
            {
                var y = ActualHeight * (1 - normalizedPeak);
                drawingContext.DrawLine(PeakPen, new WpfPoint(-1, y), new WpfPoint(ActualWidth + 1, y));
            }
            else
            {
                var x = ActualWidth * normalizedPeak;
                drawingContext.DrawLine(PeakPen, new WpfPoint(x, -1), new WpfPoint(x, ActualHeight + 1));
            }
        }
    }

    private System.Windows.Media.Brush BrushForDb(double db) =>
        MeterBandSelector.Select(db, MidLevelThresholdDb, HighLevelThresholdDb) switch
        {
            2 => HighLevelBrush,
            1 => MidLevelBrush,
            _ => LowLevelBrush
        };

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
