using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MicMeter.Audio;
using MicMeter.Controls;
using MicMeter.Models;
using MicMeter.Services;

namespace MicMeter;

public partial class MainWindow : Window
{
    private static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan TrayMeterInterval = TimeSpan.FromMilliseconds(100);
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly IAudioService _microphones = PlatformServices.CreateAudioService();
    private readonly Dictionary<string, MeterRow> _rows = [];
    private readonly Dictionary<string, MeterBallistics> _ballistics = [];
    private readonly Dictionary<string, PeakHoldTracker> _peakHolds = [];
    private readonly Dictionary<string, ClippingTracker> _clippingTrackers = [];
    private readonly Dictionary<string, bool> _lastMuteStates = [];
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly MuteSoundService _muteSoundService;
    private readonly MuteNotificationWindow _muteNotificationWindow;
    private readonly DispatcherTimer _meterTimer;
    private readonly IStatusIcon _statusIcon;
    private readonly byte[] _applicationIconPng;
    private readonly DispatcherTimer _trayClickTimer;
    private byte[]? _dynamicTrayIconPng;
    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _nextTrayIconUpdate = DateTime.MinValue;
    private DateTime _nextReconnect = DateTime.MinValue;
    private DateTime _nextHotkeyRegistrationAttempt = DateTime.MinValue;
    private bool _reallyClosing;
    private bool _isCompact;
    private SettingsWindow? _settingsWindow;
    private AppTheme _appliedTheme = AppTheme.MidnightGlass;
    private double _appliedScale = -1;
    private MeterDisplayOrientation _appliedOrientation = (MeterDisplayOrientation)(-1);
    private int _appliedSegmentCount = -1;
    private uint _appliedHotkeyModifiers = uint.MaxValue;
    private int _appliedHotkeyVirtualKey = -1;
    private OverlayPlacement _appliedPlacement = (OverlayPlacement)(-1);
    private AppLanguage _appliedLanguage = AppLanguage.Japanese;
    private bool _settingsInitialized;
    private string? _appliedListeningOutputDevice = "\u0000unset";
    private string _appliedAppearanceSignature = string.Empty;

    public MainWindow(SettingsStore settingsStore, AppSettings settings)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        _settings = settings;
        _settings.Migrate();
        _appliedLanguage = _settings.UiLanguage;
        _muteSoundService = new MuteSoundService();
        _muteNotificationWindow = new MuteNotificationWindow(_settings, _settingsStore);
        _hotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(ToggleMuteAll);

        _meterTimer = new DispatcherTimer { Interval = MeterInterval };
        _meterTimer.Tick += MeterTimer_Tick;
        _meterTimer.Start();

        _trayClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _trayClickTimer.Tick += (_, _) =>
        {
            _trayClickTimer.Stop();
            ToggleMuteAll();
        };

        _applicationIconPng = LoadApplicationIconPng();
        _statusIcon = CreateStatusIcon();

        Opened += (_, _) =>
        {
            ApplyPlacement();
            UpdateAdaptiveMode();
            ConfigureHotkey();
        };
        PositionChanged += (_, _) => Window_PositionChanged();
        SizeChanged += (_, _) => UpdateAdaptiveMode();

        ApplySettings(reconnect: true);
    }

    private IStatusIcon CreateStatusIcon()
    {
        var actions = new StatusIconActions
        {
            ShowHide = ToggleVisibility,
            ToggleMute = ToggleMuteAll,
            Settings = OpenSettings,
            Quit = ExitApplication
        };
        IStatusIcon icon =
#if WINDOWS
            new WindowsStatusIcon(_applicationIconPng, actions, T);
#else
            new MacStatusIcon(_applicationIconPng, actions, T);
#endif
        icon.LeftClicked += OnStatusIconLeftClicked;
        return icon;
    }

    private void OnStatusIconLeftClicked(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayClickTimer.IsEnabled)
            {
                _trayClickTimer.Stop();
                ToggleVisibility();
            }
            else
            {
                _trayClickTimer.Start();
            }
        });
    }

    private void MeterTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;
        var hasStoppedMonitor = false;
        var highestDb = LevelMath.MinimumDb;
        var mutedCount = 0;
        var clippingCount = 0;
        bool? latestMuteChange = null;
        double? topDeviceDb = null;
        var topDeviceMuted = false;
        var topDeviceConnected = false;
        var topDeviceClipping = false;
        string? topDeviceName = null;
        var topMonitor = _microphones.Monitors.FirstOrDefault();

        if (_settingsWindow is null && _settings.MuteHotkeyEnabled && !_hotkey.IsRegistered &&
            now >= _nextHotkeyRegistrationAttempt)
        {
            _nextHotkeyRegistrationAttempt = now.AddSeconds(10);
            ConfigureHotkey();
        }

        foreach (var monitor in _microphones.Monitors)
        {
            if (!_rows.TryGetValue(monitor.DeviceId, out var row) ||
                !_ballistics.TryGetValue(monitor.DeviceId, out var ballistics) ||
                !_peakHolds.TryGetValue(monitor.DeviceId, out var peakHold) ||
                !_clippingTrackers.TryGetValue(monitor.DeviceId, out var clippingTracker))
            {
                continue;
            }

            var isTopDevice = ReferenceEquals(monitor, topMonitor);
            if (isTopDevice)
            {
                topDeviceName = monitor.DeviceName;
            }

            if (!monitor.IsRunning)
            {
                row.ShowDisconnected();
                hasStoppedMonitor = true;
                continue;
            }

            var isMuted = monitor.IsMuted;
            if (_lastMuteStates.TryGetValue(monitor.DeviceId, out var previousMuteState) &&
                previousMuteState != isMuted)
            {
                latestMuteChange = isMuted;
            }

            _lastMuteStates[monitor.DeviceId] = isMuted;
            var db = ballistics.Update(LevelMath.PeakToDb(monitor.ConsumePeak()), elapsed);
            var peakDb = peakHold.Update(db, elapsed);
            var isClipping = clippingTracker.Update(db, elapsed);
            row.Update(db, peakDb, isMuted, isClipping, monitor.IsListening);
            if (isTopDevice)
            {
                topDeviceDb = db;
                topDeviceMuted = isMuted;
                topDeviceConnected = true;
                topDeviceClipping = isClipping;
            }

            highestDb = Math.Max(highestDb, db);
            if (isMuted)
            {
                mutedCount++;
            }

            if (isClipping && _settings.ShowClippingWarning)
            {
                clippingCount++;
            }
        }

        var noConnectedDevices = _microphones.Monitors.Count == 0;
        var selectedDeviceIsMissing = _settings.RoutingMode != AudioRoutingMode.Plugin &&
                                      _microphones.Monitors.Count < _settings.DeviceIds.Count;
        if ((noConnectedDevices || hasStoppedMonitor || selectedDeviceIsMissing) && now >= _nextReconnect)
        {
            _nextReconnect = now.AddSeconds(2);
            var availableIds = selectedDeviceIsMissing
                ? _microphones.GetCaptureDevices().Select(device => device.Id).ToHashSet()
                : [];
            var missingDevicesAreBack = selectedDeviceIsMissing &&
                                        _settings.DeviceIds.All(availableIds.Contains);
            if (noConnectedDevices || hasStoppedMonitor || missingDevicesAreBack)
            {
                ConnectDevices();
            }
        }

        _statusIcon.SetToolTip(clippingCount > 0
            ? $"MicMeter - CLIP! ({clippingCount})"
            : mutedCount == _microphones.Monitors.Count && mutedCount > 0
                ? T("MicMeter - すべてミュート", "MicMeter - All muted")
                : $"MicMeter - {highestDb:0.0} dB / {_microphones.Monitors.Count} device(s)");

        if (now >= _nextTrayIconUpdate)
        {
            _nextTrayIconUpdate = now + TrayMeterInterval;
            UpdateTrayMeter(topDeviceDb ?? LevelMath.MinimumDb, topDeviceMuted, topDeviceConnected,
                topDeviceClipping);
        }

        if (_settings.ShowTrayMeter)
        {
            var topState = !topDeviceConnected
                ? T("切断", "Disconnected")
                : topDeviceMuted ? T("ミュート", "Muted") : $"{topDeviceDb:0.0} dB";
            var tooltip = string.IsNullOrWhiteSpace(topDeviceName)
                ? $"MicMeter - {topState}"
                : $"{topDeviceName} - {topState}";
            _statusIcon.SetToolTip(tooltip.Length <= 63 ? tooltip : tooltip[..63]);
        }

        if (latestMuteChange.HasValue)
        {
            if (_settings.PlayMuteSounds)
            {
                _muteSoundService.Play(latestMuteChange.Value);
            }

            UpdateMuteNotification();
        }
    }

    private void UpdateTrayMeter(double db, bool isMuted, bool isConnected, bool isClipping)
    {
        if (!_settings.ShowTrayMeter)
        {
            if (_dynamicTrayIconPng is not null)
            {
                _statusIcon.SetIcon(_applicationIconPng);
                _dynamicTrayIconPng = null;
            }

            return;
        }

        var nextIcon = TrayMeterIconRenderer.CreatePng(db, isMuted, isConnected, isClipping,
            _settings.LowLevelColor, _settings.MidLevelColor, _settings.HighLevelColor,
            _settings.MidLevelThresholdDb, _settings.HighLevelThresholdDb);
        _dynamicTrayIconPng = nextIcon;
        _statusIcon.SetIcon(nextIcon);
    }

    private void ApplySettings(bool reconnect)
    {
        Opacity = _settings.Opacity;
        Topmost = _settings.Topmost;

        if (!_settingsInitialized || _settings.Theme != _appliedTheme)
        {
            ApplyTheme();
            _appliedTheme = _settings.Theme;
        }

        if (reconnect)
        {
            ConnectDevices();
        }

        var scaleChanged = Math.Abs(_settings.Scale - _appliedScale) > 0.001;
        var layoutChanged = scaleChanged ||
                            _settings.DisplayOrientation != _appliedOrientation ||
                            _settings.SegmentCount != _appliedSegmentCount;
        if (!_settingsInitialized || layoutChanged)
        {
            ResizeForRows();
            _appliedScale = _settings.Scale;
            _appliedOrientation = _settings.DisplayOrientation;
            _appliedSegmentCount = _settings.SegmentCount;
        }

        if (!_settingsInitialized ||
            _settings.MuteHotkeyModifiers != _appliedHotkeyModifiers ||
            _settings.MuteHotkeyVirtualKey != _appliedHotkeyVirtualKey)
        {
            ConfigureHotkey();
            _appliedHotkeyModifiers = _settings.MuteHotkeyModifiers;
            _appliedHotkeyVirtualKey = _settings.MuteHotkeyVirtualKey;
        }

        if (!_settingsInitialized ||
            _settings.ListeningOutputDeviceId != _appliedListeningOutputDevice)
        {
            _microphones.SetListeningOutputDevice(_settings.ListeningOutputDeviceId);
            _appliedListeningOutputDevice = _settings.ListeningOutputDeviceId;
        }

        if (!_settingsInitialized || _settings.Placement != _appliedPlacement)
        {
            if (IsLoaded)
            {
                ApplyPlacement();
            }
            _appliedPlacement = _settings.Placement;
        }

        var appearance = AppearanceSignature();
        if (!_settingsInitialized || appearance != _appliedAppearanceSignature)
        {
            RefreshRows();
            _appliedAppearanceSignature = appearance;
        }
        _settingsInitialized = true;
    }

    private string AppearanceSignature() =>
        string.Join('|',
            _settings.LowLevelColor,
            _settings.MidLevelColor,
            _settings.HighLevelColor,
            _settings.MidLevelThresholdDb,
            _settings.HighLevelThresholdDb,
            _settings.SegmentCount,
            _settings.ShowDeviceName,
            _settings.ShowLevelText,
            _settings.ShowStatusText,
            _settings.ShowMuteControl,
            _settings.ShowListeningControl,
            _settings.ShowPeakHold,
            _settings.ShowClippingWarning);

    private void RefreshRows()
    {
        foreach (var row in RowsPanel.Children.OfType<MeterRow>())
        {
            row.RefreshAppearance();
            row.ApplyDisplayMode(_isCompact);
        }
    }

    private void ApplyTheme()
    {
        if (_settings.Theme == AppTheme.FlatBlack)
        {
            ShellBorder.Margin = new Thickness(0);
            ShellBorder.CornerRadius = new CornerRadius(0);
            ShellBorder.BorderThickness = new Thickness(0);
            ShellBorder.Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            ShellBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48));
            ShellBorder.Effect = null;
            return;
        }

        ShellBorder.Margin = new Thickness(8);
        ShellBorder.CornerRadius = new CornerRadius(14);
        ShellBorder.BorderThickness = new Thickness(1);
        ShellBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x6B, 0x34, 0x49, 0x5E));
        ShellBorder.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xF2, 0x16, 0x20, 0x2B), 0),
                new GradientStop(Color.FromArgb(0xF2, 0x0A, 0x0F, 0x16), 1)
            }
        };
        ShellBorder.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 16,
            OffsetX = 0,
            OffsetY = 3,
            Opacity = 0.55
        };
    }

    private void ConnectDevices()
    {
        _microphones.SetBufferFrameSize(_settings.BufferFrameSize);
        _microphones.SetRoutingMode(_settings.RoutingMode);
        var connectedIds = _microphones.Connect(_settings.DeviceIds);
        if (_settings.DeviceIds.Count == 0)
        {
            _settings.DeviceIds = connectedIds.ToList();
        }

        _microphones.SetLoopbackDevices(_settings.LoopbackDeviceIds);

        RowsPanel.Children.Clear();
        _rows.Clear();
        _ballistics.Clear();
        _peakHolds.Clear();
        _clippingTrackers.Clear();
        _lastMuteStates.Clear();
        RowsPanel.Orientation = _settings.DisplayOrientation == MeterDisplayOrientation.Vertical
            ? Avalonia.Layout.Orientation.Horizontal
            : Avalonia.Layout.Orientation.Vertical;
        foreach (var monitor in _microphones.Monitors)
        {
            var row = new MeterRow(monitor.DeviceId, monitor.DeviceName, _settings);
            row.ToggleMuteRequested += (_, _) =>
            {
                _microphones.ToggleMute(row.DeviceId);
                MeterTimer_Tick(null, EventArgs.Empty);
            };
            row.ToggleListeningRequested += (_, _) =>
            {
                _microphones.ToggleListening(row.DeviceId);
                MeterTimer_Tick(null, EventArgs.Empty);
            };
            RowsPanel.Children.Add(row);
            _rows[monitor.DeviceId] = row;
            _ballistics[monitor.DeviceId] = new MeterBallistics();
            _peakHolds[monitor.DeviceId] = new PeakHoldTracker();
            _clippingTrackers[monitor.DeviceId] = new ClippingTracker();
            _lastMuteStates[monitor.DeviceId] = monitor.IsMuted;
        }

        if (_microphones.Monitors.Count == 0)
        {
            var unavailable = new MeterRow(string.Empty, "入力デバイスに接続できません", _settings);
            unavailable.ShowDisconnected();
            RowsPanel.Children.Add(unavailable);
        }

        Title = $"MicMeter - {_microphones.Monitors.Count} device(s)";
        ResizeForRows();
        UpdateAdaptiveMode();
        Dispatcher.UIThread.Post(UpdateMuteNotification, DispatcherPriority.Loaded);
    }

    private void ResizeForRows()
    {
        UpdateLayoutConstraints();
        var rowCount = Math.Max(1, RowsPanel.Children.Count);
        var defaultWidth = (_settings.DisplayOrientation == MeterDisplayOrientation.Vertical
            ? (rowCount * 104) + 32
            : 376) * _settings.Scale;
        var defaultHeight = (_settings.DisplayOrientation == MeterDisplayOrientation.Vertical
            ? 280
            : (rowCount * 56) + 32) * _settings.Scale;
        var area = GetWorkingArea();
        Width = Math.Clamp(_settings.WindowWidth ?? defaultWidth, MinWidth, area.Width);
        Height = Math.Clamp(_settings.WindowHeight ?? defaultHeight, MinHeight, area.Height);
        Dispatcher.UIThread.Post(LayoutRows, DispatcherPriority.Loaded);
    }

    private void UpdateAdaptiveMode()
    {
        var compact = AdaptiveLayoutDecider.ShouldUseCompactMode(
            _settings.DisplayOrientation,
            Width,
            Height,
            Math.Max(1, RowsPanel.Children.Count));
        _isCompact = compact;
        foreach (var row in RowsPanel.Children.OfType<MeterRow>())
        {
            row.ApplyDisplayMode(compact);
        }

        UpdateLayoutConstraints();
        // Defer row layout until the current layout pass has settled so
        // ShellBorder.Bounds is final; running immediately reads stale bounds
        // and produces a broken overlay until the next resize.
        Dispatcher.UIThread.Post(LayoutRows, DispatcherPriority.Loaded);
    }

    private void UpdateLayoutConstraints()
    {
        var rowCount = Math.Max(1, RowsPanel.Children.Count);
        if (_isCompact)
        {
            RowsPanel.Orientation = Avalonia.Layout.Orientation.Vertical;
            MinWidth = 80;
            MinHeight = Math.Max(30, (rowCount * 12) + 18);
        }
        else if (_settings.DisplayOrientation == MeterDisplayOrientation.Vertical)
        {
            RowsPanel.Orientation = Avalonia.Layout.Orientation.Horizontal;
            MinWidth = 80;
            MinHeight = 150;
        }
        else
        {
            RowsPanel.Orientation = Avalonia.Layout.Orientation.Vertical;
            MinWidth = 80;
            MinHeight = 44;
        }
    }

    private void LayoutRows()
    {
        var rows = RowsPanel.Children.OfType<MeterRow>().ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var availableWidth = Math.Max(20, ShellBorder.Bounds.Width - ShellBorder.Padding.Left - ShellBorder.Padding.Right);
        var availableHeight = Math.Max(16, ShellBorder.Bounds.Height - ShellBorder.Padding.Top - ShellBorder.Padding.Bottom);
        foreach (var row in rows)
        {
            if (_isCompact || _settings.DisplayOrientation == MeterDisplayOrientation.Horizontal)
            {
                row.Width = double.NaN;
                row.Height = availableHeight / rows.Length;
            }
            else
            {
                row.Width = availableWidth / rows.Length;
                row.Height = availableHeight;
            }
        }
    }

    private void ApplyPlacement()
    {
        var area = GetWorkingArea();
        const double margin = 10;
        var left = _settings.Placement switch
        {
            OverlayPlacement.BottomLeft or OverlayPlacement.TopLeft => area.X + margin,
            OverlayPlacement.BottomCenter or OverlayPlacement.TopCenter => area.X + ((area.Width - Width) / 2),
            OverlayPlacement.Custom when _settings.CustomLeft.HasValue => _settings.CustomLeft.Value,
            _ => area.Right - Width - margin
        };
        var top = _settings.Placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.TopCenter or OverlayPlacement.TopRight => area.Y + margin,
            OverlayPlacement.Custom when _settings.CustomTop.HasValue => _settings.CustomTop.Value,
            _ => area.Bottom - Height - margin
        };
        SetPosition(left, top);
    }

    private void ToggleMuteAll()
    {
        var shouldMute = _microphones.Monitors.Count == 0 ||
                         _microphones.Monitors.Any(monitor => !monitor.IsMuted);
        if (_microphones.Monitors.Any(monitor => !monitor.IsRunning) ||
            _microphones.Monitors.Count < _settings.DeviceIds.Count)
        {
            ConnectDevices();
        }

        _microphones.SetMuteAll(shouldMute);
        if (_microphones.Monitors.Any(monitor => !monitor.IsRunning))
        {
            ConnectDevices();
            _microphones.SetMuteAll(shouldMute);
        }

        MeterTimer_Tick(null, EventArgs.Empty);
    }

    private void ConfigureHotkey()
    {
        _hotkey.Unregister();
        if (_settings.MuteHotkeyEnabled)
        {
            _hotkey.Register(_settings.MuteHotkeyModifiers, _settings.MuteHotkeyVirtualKey);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (FindInteractiveAncestor(e.Source))
        {
            return;
        }

        BeginMoveDrag(e);
        _settings.Placement = OverlayPlacement.Custom;
        SaveCustomPosition();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            OpenSettings();
        }
    }

    private static bool FindInteractiveAncestor(object? source)
    {
        var current = source as Visual;
        while (current is not null)
        {
            if (current is Button or Thumb)
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private void Window_PositionChanged()
    {
        if (_settings.Placement == OverlayPlacement.Custom && IsLoaded)
        {
            SaveCustomPosition();
        }
    }

    private void SaveCustomPosition()
    {
        _settings.CustomLeft = GetLeft();
        _settings.CustomTop = GetTop();
        _settingsStore.Save(_settings);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var devices = _microphones.GetCaptureDevices();
        var outputDevices = _microphones.GetOutputDevices();
        var dialog = new SettingsWindow(_settings, devices, outputDevices, ExitApplication);
        _settingsWindow = dialog;
        _hotkey.Unregister();
        dialog.MuteOverlayPlacementRequested += (_, _) => _muteNotificationWindow.ShowPlacementPreview();
        dialog.SettingsChanged += (_, args) =>
        {
            ApplySettings(reconnect: args.RequiresReconnect);
            if (_settings.UiLanguage != _appliedLanguage)
            {
                UpdateTrayMenuLanguage();
                _appliedLanguage = _settings.UiLanguage;
            }
            if (_muteNotificationWindow.IsPlacementPreview)
            {
                _muteNotificationWindow.EndPlacementPreview();
            }

            UpdateMuteNotification();
        };
        dialog.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _settingsStore.Save(_settings);
            if (_muteNotificationWindow.IsPlacementPreview)
            {
                _muteNotificationWindow.EndPlacementPreview();
            }

            UpdateMuteNotification();
            ConfigureHotkey();
        };
        dialog.Show(this);
    }

    private void ResizeThumb_DragDelta(object? sender, VectorEventArgs e)
    {
        var area = GetWorkingArea();
        Width = Math.Clamp(Width + e.Vector.X, MinWidth, area.Width);
        Height = Math.Clamp(Height + e.Vector.Y, MinHeight, area.Height);
    }

    private void ResizeThumb_DragCompleted(object? sender, VectorEventArgs e)
    {
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settingsStore.Save(_settings);
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    private void UpdateTrayMenuLanguage()
    {
        _statusIcon.UpdateLanguage();
    }

    private void UpdateMuteNotification()
    {
        if (_muteNotificationWindow.IsPlacementPreview)
        {
            return;
        }

        if (!_settings.ShowMuteOverlay)
        {
            _muteNotificationWindow.HideStatus();
            return;
        }

        var mutedMonitors = _microphones.Monitors.Where(monitor => monitor.IsMuted).ToArray();
        if (mutedMonitors.Length == 0)
        {
            _muteNotificationWindow.HideStatus();
            return;
        }

        var allMuted = mutedMonitors.Length == _microphones.Monitors.Count;
        var detailName = mutedMonitors.Length == 1 ? mutedMonitors[0].DeviceName : null;
        _muteNotificationWindow.ShowMuted(detailName, allMuted);
    }

    private string T(string japanese, string english) =>
        _settings.UiLanguage == AppLanguage.English ? english : japanese;

    private void ExitApplication()
    {
        _reallyClosing = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_reallyClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _meterTimer.Stop();
        _trayClickTimer.Stop();
        _settingsStore.Save(_settings);
        _statusIcon.Dispose();
        _dynamicTrayIconPng = null;
        _muteNotificationWindow.Close();
        _muteSoundService.Dispose();
        _hotkey.Dispose();
        _microphones.Dispose();
        base.OnClosing(e);
    }

    private double GetLeft()
    {
        var screen = Screens.Primary ?? Screens.All[0];
        return Position.X / screen.Scaling;
    }

    private double GetTop()
    {
        var screen = Screens.Primary ?? Screens.All[0];
        return Position.Y / screen.Scaling;
    }

    private void SetPosition(double left, double top)
    {
        var screen = Screens.Primary ?? Screens.All[0];
        Position = new PixelPoint((int)Math.Round(left * screen.Scaling), (int)Math.Round(top * screen.Scaling));
    }

    private Rect GetWorkingArea()
    {
        var screen = Screens.Primary ?? Screens.All[0];
        return new Rect(
            screen.WorkingArea.X / screen.Scaling,
            screen.WorkingArea.Y / screen.Scaling,
            screen.WorkingArea.Width / screen.Scaling,
            screen.WorkingArea.Height / screen.Scaling);
    }

    private static byte[] LoadApplicationIconPng()
    {
        using var stream = AssetLoader.Open(new Uri("avares://MicMeter/Assets/app-icon.png"));
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
