using System.Windows;
using System.Windows.Media;
using MicMeter.Models;
using MicMeter.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace MicMeter.Controls;

public partial class MeterRow : System.Windows.Controls.UserControl
{
    private static readonly MediaBrush ClipBrush = CreateBrush(248, 77, 77);
    private static readonly MediaBrush LiveBrush = CreateBrush(52, 211, 107);
    private static readonly MediaBrush ListenBrush = CreateBrush(80, 170, 255);
    private static readonly MediaBrush ListenButtonBrush = CreateBrush(38, 92, 145);
    private static readonly MediaBrush InactiveDotBrush = CreateBrush(85, 97, 107);
    private static readonly System.Windows.Media.FontFamily IconFont = new("Segoe MDL2 Assets");
    private static readonly System.Windows.Media.FontFamily TextFont = new("Segoe UI");
    private readonly AppSettings _settings;

    public MeterRow(string deviceId, string deviceName, AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DeviceId = deviceId;
        HorizontalDeviceNameText.Text = deviceName;
        VerticalDeviceNameText.Text = deviceName;
        ToolTip = deviceName;
        foreach (var meter in new[] { HorizontalMeter, VerticalMeter, CompactMeter })
        {
            meter.SegmentCount = settings.SegmentCount;
            meter.LowLevelBrush = ParseBrush(settings.LowLevelColor, 46, 230, 166);
            meter.MidLevelBrush = ParseBrush(settings.MidLevelColor, 255, 200, 87);
            meter.HighLevelBrush = ParseBrush(settings.HighLevelColor, 255, 93, 115);
            meter.MidLevelThresholdDb = settings.MidLevelThresholdDb;
            meter.HighLevelThresholdDb = settings.HighLevelThresholdDb;
        }

        if (settings.Theme == AppTheme.FlatBlack)
        {
            foreach (var border in new[] { HorizontalBorder, VerticalBorder, CompactBorder })
            {
                border.CornerRadius = new CornerRadius(0);
                border.Background = MediaBrushes.Transparent;
            }
        }

        IsVertical = settings.DisplayOrientation == MeterDisplayOrientation.Vertical;
        ApplyDisplayMode(false);
    }

    public string DeviceId { get; }
    public bool IsVertical { get; }
    public event EventHandler? ToggleMuteRequested;
    public event EventHandler? ToggleListeningRequested;

    public void ApplyDisplayMode(bool compact)
    {
        CompactBorder.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        HorizontalBorder.Visibility = !compact && !IsVertical ? Visibility.Visible : Visibility.Collapsed;
        VerticalBorder.Visibility = !compact && IsVertical ? Visibility.Visible : Visibility.Collapsed;

        HorizontalDeviceNameText.Visibility = _settings.ShowDeviceName ? Visibility.Visible : Visibility.Collapsed;
        VerticalDeviceNameText.Visibility = _settings.ShowDeviceName ? Visibility.Visible : Visibility.Collapsed;
        HorizontalNameRow.Height = new GridLength(_settings.ShowDeviceName ? 15 : 0);
        VerticalNameRow.Height = new GridLength(_settings.ShowDeviceName ? 28 : 0);

        HorizontalLevelText.Visibility = VerticalLevelText.Visibility =
            _settings.ShowLevelText ? Visibility.Visible : Visibility.Collapsed;
        HorizontalStatusText.Visibility = VerticalStatusText.Visibility =
            _settings.ShowStatusText ? Visibility.Visible : Visibility.Collapsed;
        HorizontalValueColumn.Width = new GridLength(
            _settings.ShowLevelText || _settings.ShowStatusText ? 80 : 0);
        VerticalLevelRow.Height = new GridLength(_settings.ShowLevelText ? 22 : 0);
        VerticalStatusRow.Height = new GridLength(_settings.ShowStatusText ? 18 : 0);

        HorizontalMuteButton.Visibility = VerticalMuteButton.Visibility =
            _settings.ShowMuteControl ? Visibility.Visible : Visibility.Collapsed;
        HorizontalListenButton.Visibility = VerticalListenButton.Visibility =
            _settings.ShowListeningControl ? Visibility.Visible : Visibility.Collapsed;
        HorizontalControlsColumn.Width = new GridLength(
            (_settings.ShowMuteControl ? 34 : 0) + (_settings.ShowListeningControl ? 34 : 0));
        VerticalControlsRow.Height = new GridLength(
            _settings.ShowMuteControl || _settings.ShowListeningControl ? 38 : 0);

        CompactMuteButton.Visibility = _settings.ShowMuteControl ? Visibility.Visible : Visibility.Collapsed;
        CompactListenButton.Visibility = _settings.ShowListeningControl ? Visibility.Visible : Visibility.Collapsed;
        CompactMuteColumn.Width = new GridLength(_settings.ShowMuteControl ? 18 : 0);
        CompactListenColumn.Width = new GridLength(_settings.ShowListeningControl ? 18 : 0);

        Height = compact ? 20 : IsVertical ? 248 : 56;
        Width = !compact && IsVertical ? 104 : double.NaN;
    }

    public void Update(double db, double peakDb, bool isMuted, bool isClipping, bool isListening)
    {
        var displayedPeak = _settings.ShowPeakHold ? peakDb : LevelMath.MinimumDb;
        foreach (var meter in new[] { HorizontalMeter, VerticalMeter, CompactMeter })
        {
            meter.LevelDb = db;
            meter.PeakDb = displayedPeak;
            meter.IsMuted = isMuted;
        }

        var showClip = isClipping && _settings.ShowClippingWarning;
        var levelText = isMuted ? "MUTED" : $"{db,5:0.0} dB";
        HorizontalLevelText.Text = VerticalLevelText.Text = levelText;
        var status = showClip ? "CLIP!" : isMuted ? "MIC OFF" : isListening ? "LISTEN" : "LIVE";
        var statusBrush = showClip ? ClipBrush : isMuted ? MediaBrushes.IndianRed : isListening ? ListenBrush : LiveBrush;
        HorizontalStatusText.Text = VerticalStatusText.Text = status;
        HorizontalStatusText.Foreground = VerticalStatusText.Foreground = statusBrush;
        HorizontalMicrophoneGlyph.FontFamily = VerticalMicrophoneGlyph.FontFamily = isMuted ? TextFont : IconFont;
        HorizontalMicrophoneGlyph.Text = VerticalMicrophoneGlyph.Text = isMuted ? "×" : "\uE720";
        HorizontalListenButton.Background = VerticalListenButton.Background = isListening ? ListenButtonBrush : MediaBrushes.Transparent;
        CompactMuteDot.Fill = isMuted ? ClipBrush : LiveBrush;
        CompactListenDot.Fill = isListening ? ListenBrush : InactiveDotBrush;
        HorizontalBorder.BorderBrush = VerticalBorder.BorderBrush = CompactBorder.BorderBrush =
            showClip ? ClipBrush : MediaBrushes.Transparent;
    }

    public void ShowDisconnected()
    {
        Update(LevelMath.MinimumDb, LevelMath.MinimumDb, true, false, false);
        HorizontalLevelText.Text = VerticalLevelText.Text = "NO MIC";
        HorizontalStatusText.Text = VerticalStatusText.Text = "RETRYING";
        HorizontalStatusText.Foreground = VerticalStatusText.Foreground = MediaBrushes.Orange;
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e) => ToggleMuteRequested?.Invoke(this, EventArgs.Empty);
    private void ListenButton_Click(object sender, RoutedEventArgs e) => ToggleListeningRequested?.Invoke(this, EventArgs.Empty);

    private static MediaBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static MediaBrush ParseBrush(string value, byte fallbackRed, byte fallbackGreen, byte fallbackBlue)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!;
            return CreateBrush(color.R, color.G, color.B);
        }
        catch
        {
            return CreateBrush(fallbackRed, fallbackGreen, fallbackBlue);
        }
    }
}
