using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MicMeter.Models;
using MicMeter.Services;
using MediaBrush = Avalonia.Media.IBrush;
using MediaBrushes = Avalonia.Media.Brushes;
using MediaColor = Avalonia.Media.Color;

namespace MicMeter.Controls;

public partial class MeterRow : UserControl
{
    private static readonly MediaBrush ClipBrush = CreateBrush(248, 77, 77);
    private static readonly MediaBrush LiveBrush = CreateBrush(52, 211, 107);
    private static readonly MediaBrush ListenBrush = CreateBrush(80, 170, 255);
    private static readonly MediaBrush ListenButtonBrush = CreateBrush(38, 92, 145);
    private static readonly MediaBrush InactiveDotBrush = CreateBrush(85, 97, 107);
    private readonly AppSettings _settings;

    public MeterRow(string deviceId, string deviceName, AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DeviceId = deviceId;
        HorizontalDeviceNameText.Text = deviceName;
        VerticalDeviceNameText.Text = deviceName;
        ToolTip.SetTip(this, deviceName);
        RefreshAppearance();

        IsVertical = settings.DisplayOrientation == MeterDisplayOrientation.Vertical;
        ApplyDisplayMode(false);
    }

    public void RefreshAppearance()
    {
        foreach (var meter in new[] { HorizontalMeter, VerticalMeter, CompactMeter })
        {
            meter.SegmentCount = _settings.SegmentCount;
            meter.LowLevelBrush = ParseBrush(_settings.LowLevelColor, 46, 230, 166);
            meter.MidLevelBrush = ParseBrush(_settings.MidLevelColor, 255, 200, 87);
            meter.HighLevelBrush = ParseBrush(_settings.HighLevelColor, 255, 93, 115);
            meter.MidLevelThresholdDb = _settings.MidLevelThresholdDb;
            meter.HighLevelThresholdDb = _settings.HighLevelThresholdDb;
        }

        if (_settings.Theme == AppTheme.FlatBlack)
        {
            foreach (var border in new[] { HorizontalBorder, VerticalBorder, CompactBorder })
            {
                border.CornerRadius = new CornerRadius(0);
                border.Background = MediaBrushes.Transparent;
            }
        }
    }

    public string DeviceId { get; }
    public bool IsVertical { get; }
    public event EventHandler? ToggleMuteRequested;
    public event EventHandler? ToggleListeningRequested;

    public void ApplyDisplayMode(bool compact)
    {
        CompactBorder.IsVisible = compact;
        HorizontalBorder.IsVisible = !compact && !IsVertical;
        VerticalBorder.IsVisible = !compact && IsVertical;

        HorizontalDeviceNameText.IsVisible = _settings.ShowDeviceName;
        VerticalDeviceNameText.IsVisible = _settings.ShowDeviceName;
        HorizontalNameGrid.RowDefinitions[0].Height = new GridLength(_settings.ShowDeviceName ? 15 : 0);
        VerticalGrid.RowDefinitions[0].Height = new GridLength(_settings.ShowDeviceName ? 28 : 0);

        var showLevelText = _settings.ShowLevelText;
        var showStatusText = _settings.ShowStatusText;
        HorizontalLevelText.IsVisible = VerticalLevelText.IsVisible = showLevelText;
        HorizontalStatusText.IsVisible = VerticalStatusText.IsVisible = showStatusText;
        HorizontalGrid.ColumnDefinitions[2].Width = new GridLength(showLevelText || showStatusText ? 80 : 0);
        VerticalGrid.RowDefinitions[2].Height = new GridLength(showLevelText ? 22 : 0);
        VerticalGrid.RowDefinitions[3].Height = new GridLength(showStatusText ? 18 : 0);

        var showMute = _settings.ShowMuteControl;
        var showListen = _settings.ShowListeningControl;
        HorizontalMuteButton.IsVisible = VerticalMuteButton.IsVisible = showMute;
        HorizontalListenButton.IsVisible = VerticalListenButton.IsVisible = showListen;
        HorizontalGrid.ColumnDefinitions[0].Width = new GridLength((showMute ? 34 : 0) + (showListen ? 34 : 0));
        VerticalGrid.RowDefinitions[4].Height = new GridLength(showMute || showListen ? 38 : 0);

        CompactMuteButton.IsVisible = showMute;
        CompactListenButton.IsVisible = showListen;
        CompactGrid.ColumnDefinitions[0].Width = new GridLength(showMute ? 18 : 0);
        CompactGrid.ColumnDefinitions[1].Width = new GridLength(showListen ? 18 : 0);

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
        HorizontalMuteSlash.IsVisible = VerticalMuteSlash.IsVisible = isMuted;
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

    private void MuteButton_Click(object? sender, RoutedEventArgs e) =>
        ToggleMuteRequested?.Invoke(this, EventArgs.Empty);

    private void ListenButton_Click(object? sender, RoutedEventArgs e) =>
        ToggleListeningRequested?.Invoke(this, EventArgs.Empty);

    private static MediaBrush CreateBrush(byte red, byte green, byte blue) =>
        new SolidColorBrush(MediaColor.FromRgb(red, green, blue));

    private static MediaBrush ParseBrush(string value, byte fallbackRed, byte fallbackGreen, byte fallbackBlue)
    {
        try
        {
            var color = MediaColor.Parse(value);
            return CreateBrush(color.R, color.G, color.B);
        }
        catch
        {
            return CreateBrush(fallbackRed, fallbackGreen, fallbackBlue);
        }
    }
}
