namespace MicMeter.Models;

public sealed class AppSettings
{
    public List<string> DeviceIds { get; set; } = [];

    // Kept for migration from settings written by MicMeter 0.1.
    public string? DeviceId { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.94;
    public int SegmentCount { get; set; } = 20;
    public OverlayPlacement Placement { get; set; } = OverlayPlacement.BottomRight;
    public double? CustomLeft { get; set; }
    public double? CustomTop { get; set; }
    public bool Topmost { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool MuteHotkeyEnabled { get; set; } = true;
    public uint MuteHotkeyModifiers { get; set; } = 0x0001 | 0x0002;
    public int MuteHotkeyVirtualKey { get; set; } = 0x4D;
    public MeterDisplayOrientation DisplayOrientation { get; set; } = MeterDisplayOrientation.Horizontal;
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool ShowDeviceName { get; set; } = true;
    public bool ShowLevelText { get; set; } = true;
    public bool ShowStatusText { get; set; } = true;
    public bool ShowMuteControl { get; set; } = true;
    public bool ShowListeningControl { get; set; } = true;
    public bool ShowPeakHold { get; set; } = true;
    public bool ShowClippingWarning { get; set; } = true;
    public bool ShowTrayMeter { get; set; } = true;
    public AppLanguage UiLanguage { get; set; } = AppLanguage.Japanese;
    public bool PlayMuteSounds { get; set; } = true;
    public bool ShowMuteOverlay { get; set; } = true;
    public double? MuteOverlayLeft { get; set; }
    public double? MuteOverlayTop { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.MidnightGlass;
    public string LowLevelColor { get; set; } = "#2EE6A6";
    public string MidLevelColor { get; set; } = "#FFC857";
    public string HighLevelColor { get; set; } = "#FF5D73";
    public double MidLevelThresholdDb { get; set; } = -12;
    public double HighLevelThresholdDb { get; set; } = -6;

    public void Migrate()
    {
        DeviceIds ??= [];
        if (DeviceIds.Count == 0 && !string.IsNullOrWhiteSpace(DeviceId))
        {
            DeviceIds.Add(DeviceId);
        }

        DeviceIds = DeviceIds.Distinct(StringComparer.Ordinal).ToList();
        DeviceId = null;
        if (!MuteHotkeyEnabled)
        {
            MuteHotkeyVirtualKey = 0;
        }

        MuteHotkeyEnabled = MuteHotkeyVirtualKey != 0;
    }
}

public enum MeterDisplayOrientation
{
    Horizontal,
    Vertical
}

public enum AppTheme
{
    MidnightGlass,
    FlatBlack
}

public enum AppLanguage
{
    Japanese,
    English
}

public enum OverlayPlacement
{
    BottomRight,
    BottomCenter,
    BottomLeft,
    TopRight,
    TopCenter,
    TopLeft,
    Custom
}
