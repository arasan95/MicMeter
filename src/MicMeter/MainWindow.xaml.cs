using System.ComponentModel;
using System.Drawing;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MicMeter.Audio;
using MicMeter.Controls;
using MicMeter.Models;
using MicMeter.Services;
using Forms = System.Windows.Forms;

namespace MicMeter;

public partial class MainWindow : Window
{
    private static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan TrayMeterInterval = TimeSpan.FromMilliseconds(100);
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly MicrophoneService _microphones = new();
    private readonly Dictionary<string, MeterRow> _rows = [];
    private readonly Dictionary<string, MeterBallistics> _ballistics = [];
    private readonly Dictionary<string, PeakHoldTracker> _peakHolds = [];
    private readonly Dictionary<string, ClippingTracker> _clippingTrackers = [];
    private readonly Dictionary<string, bool> _lastMuteStates = [];
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly MuteSoundService _muteSoundService;
    private readonly MuteNotificationWindow _muteNotificationWindow;
    private readonly DispatcherTimer _meterTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Icon _applicationIcon;
    private readonly DispatcherTimer _trayClickTimer;
    private Icon? _dynamicTrayIcon;
    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _nextTrayIconUpdate = DateTime.MinValue;
    private DateTime _nextReconnect = DateTime.MinValue;
    private DateTime _nextHotkeyRegistrationAttempt = DateTime.MinValue;
    private bool _reallyClosing;
    private bool _isCompact;
    private SettingsWindow? _settingsWindow;

    public MainWindow(SettingsStore settingsStore, AppSettings settings)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        _settings = settings;
        _settings.Migrate();
        _muteSoundService = new MuteSoundService();
        _muteNotificationWindow = new MuteNotificationWindow(_settings, _settingsStore);
        _hotkey.Pressed += (_, _) => ToggleMuteAll();
        SourceInitialized += (_, _) => ConfigureHotkey();
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        ApplySettings(reconnect: true);

        _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = MeterInterval };
        _meterTimer.Tick += MeterTimer_Tick;
        _meterTimer.Start();

        _trayClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _trayClickTimer.Tick += (_, _) =>
        {
            _trayClickTimer.Stop();
            ToggleMuteAll();
        };
        _applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ??
                           (System.Drawing.Icon)SystemIcons.Application.Clone();
        _trayIcon = CreateTrayIcon();
        Loaded += (_, _) =>
        {
            ApplyPlacement();
            UpdateAdaptiveMode();
        };
        LocationChanged += Window_LocationChanged;
        SizeChanged += (_, _) => UpdateAdaptiveMode();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(T("表示 / 非表示", "Show / Hide"), null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add(T("すべてミュート切り替え", "Toggle mute all"), null, (_, _) => Dispatcher.Invoke(ToggleMuteAll));
        menu.Items.Add(T("設定", "Settings"), null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(T("終了", "Exit"), null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var icon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "MicMeter",
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(() =>
                {
                    _trayClickTimer.Stop();
                    _trayClickTimer.Start();
                });
            }
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(() =>
        {
            _trayClickTimer.Stop();
            ToggleVisibility();
        });
        return icon;
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
        var selectedDeviceIsMissing = _microphones.Monitors.Count < _settings.DeviceIds.Count;
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

        _trayIcon.Text = clippingCount > 0
            ? $"MicMeter - CLIP! ({clippingCount})"
            : mutedCount == _microphones.Monitors.Count && mutedCount > 0
            ? T("MicMeter - すべてミュート", "MicMeter - All muted")
            : $"MicMeter - {highestDb:0.0} dB / {_microphones.Monitors.Count} device(s)";

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
            _trayIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
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
            if (_dynamicTrayIcon is not null)
            {
                _trayIcon.Icon = _applicationIcon;
                _dynamicTrayIcon.Dispose();
                _dynamicTrayIcon = null;
            }
            return;
        }

        var nextIcon = TrayMeterIconRenderer.Create(db, isMuted, isConnected, isClipping,
            _settings.LowLevelColor, _settings.MidLevelColor, _settings.HighLevelColor,
            _settings.MidLevelThresholdDb, _settings.HighLevelThresholdDb);
        var previousIcon = _dynamicTrayIcon;
        _dynamicTrayIcon = nextIcon;
        _trayIcon.Icon = nextIcon;
        previousIcon?.Dispose();

    }

    private void ApplySettings(bool reconnect)
    {
        ApplyTheme();
        Opacity = _settings.Opacity;
        Topmost = _settings.Topmost;
        if (reconnect)
        {
            ConnectDevices();
        }

        if (new System.Windows.Interop.WindowInteropHelper(this).Handle != 0)
        {
            ConfigureHotkey();
        }

        ResizeForRows();
        if (IsLoaded)
        {
            ApplyPlacement();
        }
    }

    private void ApplyTheme()
    {
        if (_settings.Theme == AppTheme.FlatBlack)
        {
            ShellBorder.Margin = new Thickness(0);
            ShellBorder.CornerRadius = new CornerRadius(0);
            ShellBorder.BorderThickness = new Thickness(0);
            ShellBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0, 0, 0));
            ShellBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(48, 48, 48));
            ShellBorder.Effect = null;
            return;
        }

        ShellBorder.Margin = new Thickness(8);
        ShellBorder.CornerRadius = new CornerRadius(14);
        ShellBorder.BorderThickness = new Thickness(1);
        ShellBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(0x6B, 0x34, 0x49, 0x5E));
        ShellBorder.Background = new System.Windows.Media.LinearGradientBrush(
            System.Windows.Media.Color.FromArgb(0xF2, 0x16, 0x20, 0x2B),
            System.Windows.Media.Color.FromArgb(0xF2, 0x0A, 0x0F, 0x16),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 1));
        ShellBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = System.Windows.Media.Colors.Black,
            BlurRadius = 16,
            ShadowDepth = 3,
            Opacity = 0.55
        };
    }

    private void ConnectDevices()
    {
        var connectedIds = _microphones.Connect(_settings.DeviceIds);
        if (_settings.DeviceIds.Count == 0)
        {
            _settings.DeviceIds = connectedIds.ToList();
        }

        RowsPanel.Children.Clear();
        _rows.Clear();
        _ballistics.Clear();
        _peakHolds.Clear();
        _clippingTrackers.Clear();
        _lastMuteStates.Clear();
        RowsPanel.Orientation = _settings.DisplayOrientation == MeterDisplayOrientation.Vertical
            ? System.Windows.Controls.Orientation.Horizontal
            : System.Windows.Controls.Orientation.Vertical;
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
        Dispatcher.BeginInvoke(UpdateMuteNotification, DispatcherPriority.Loaded);
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
        Width = Math.Clamp(_settings.WindowWidth ?? defaultWidth, MinWidth, SystemParameters.WorkArea.Width);
        Height = Math.Clamp(_settings.WindowHeight ?? defaultHeight, MinHeight, SystemParameters.WorkArea.Height);
        Dispatcher.BeginInvoke(LayoutRows, DispatcherPriority.Loaded);
    }

    private void UpdateAdaptiveMode()
    {
        var compact = AdaptiveLayoutDecider.ShouldUseCompactMode(
            _settings.DisplayOrientation,
            ActualWidth,
            ActualHeight,
            Math.Max(1, RowsPanel.Children.Count));
        _isCompact = compact;
        foreach (var row in RowsPanel.Children.OfType<MeterRow>())
        {
            row.ApplyDisplayMode(compact);
        }

        UpdateLayoutConstraints();
        LayoutRows();
        Dispatcher.BeginInvoke(LayoutRows, DispatcherPriority.Render);
    }

    private void UpdateLayoutConstraints()
    {
        var rowCount = Math.Max(1, RowsPanel.Children.Count);
        if (_isCompact)
        {
            RowsPanel.Orientation = System.Windows.Controls.Orientation.Vertical;
            MinWidth = 80;
            MinHeight = Math.Max(30, (rowCount * 12) + 18);
        }
        else if (_settings.DisplayOrientation == MeterDisplayOrientation.Vertical)
        {
            RowsPanel.Orientation = System.Windows.Controls.Orientation.Horizontal;
            MinWidth = 80;
            MinHeight = 150;
        }
        else
        {
            RowsPanel.Orientation = System.Windows.Controls.Orientation.Vertical;
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

        var availableWidth = Math.Max(20, ShellBorder.ActualWidth - ShellBorder.Padding.Left - ShellBorder.Padding.Right);
        var availableHeight = Math.Max(16, ShellBorder.ActualHeight - ShellBorder.Padding.Top - ShellBorder.Padding.Bottom);
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
        var area = SystemParameters.WorkArea;
        const double margin = 10;
        Left = _settings.Placement switch
        {
            OverlayPlacement.BottomLeft or OverlayPlacement.TopLeft => area.Left + margin,
            OverlayPlacement.BottomCenter or OverlayPlacement.TopCenter => area.Left + ((area.Width - ActualWidth) / 2),
            OverlayPlacement.Custom when _settings.CustomLeft.HasValue => _settings.CustomLeft.Value,
            _ => area.Right - ActualWidth - margin
        };
        Top = _settings.Placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.TopCenter or OverlayPlacement.TopRight => area.Top + margin,
            OverlayPlacement.Custom when _settings.CustomTop.HasValue => _settings.CustomTop.Value,
            _ => area.Bottom - ActualHeight - margin
        };
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

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            RecoverAfterIdle();
        }
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect)
        {
            RecoverAfterIdle();
        }
    }

    private async void RecoverAfterIdle()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (_reallyClosing)
        {
            return;
        }
        await Dispatcher.InvokeAsync(() =>
        {
            ConnectDevices();
            ConfigureHotkey();
        });
    }

    private void ConfigureHotkey()
    {
        _hotkey.Unregister();
        if (_settings.MuteHotkeyEnabled)
        {
            _hotkey.Register(this, _settings.MuteHotkeyModifiers, _settings.MuteHotkeyVirtualKey);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || FindButtonAncestor(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
            _settings.Placement = OverlayPlacement.Custom;
            SaveCustomPosition();
        }
        catch (InvalidOperationException)
        {
            // The mouse may be released before DragMove starts.
        }
    }

    private static bool FindButtonAncestor(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button or Thumb)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => OpenSettings();

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_settings.Placement == OverlayPlacement.Custom && IsLoaded)
        {
            SaveCustomPosition();
        }
    }

    private void SaveCustomPosition()
    {
        _settings.CustomLeft = Left;
        _settings.CustomTop = Top;
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
        var dialog = new SettingsWindow(_settings, devices) { Owner = this };
        _settingsWindow = dialog;
        _hotkey.Unregister();
        dialog.MuteOverlayPlacementRequested += (_, _) => _muteNotificationWindow.ShowPlacementPreview();
        dialog.SettingsChanged += (_, args) =>
        {
            ApplySettings(reconnect: args.RequiresReconnect);
            UpdateTrayMenuLanguage();
            if (_muteNotificationWindow.IsPlacementPreview)
            {
                _muteNotificationWindow.EndPlacementPreview();
            }
            UpdateMuteNotification();
            _settingsStore.Save(_settings);
        };
        dialog.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (_muteNotificationWindow.IsPlacementPreview)
            {
                _muteNotificationWindow.EndPlacementPreview();
            }
            UpdateMuteNotification();
            ConfigureHotkey();
        };
        dialog.Show();
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, SystemParameters.WorkArea.Width);
        Height = Math.Clamp(Height + e.VerticalChange, MinHeight, SystemParameters.WorkArea.Height);
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
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
        var items = _trayIcon.ContextMenuStrip?.Items;
        if (items is null || items.Count < 5)
        {
            return;
        }

        items[0].Text = T("表示 / 非表示", "Show / Hide");
        items[1].Text = T("すべてミュート切り替え", "Toggle mute all");
        items[2].Text = T("設定", "Settings");
        items[4].Text = T("終了", "Exit");
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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _meterTimer.Stop();
        _trayClickTimer.Stop();
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        _settingsStore.Save(_settings);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _dynamicTrayIcon?.Dispose();
        _applicationIcon.Dispose();
        _muteNotificationWindow.Close();
        _muteSoundService.Dispose();
        _hotkey.Dispose();
        _microphones.Dispose();
        base.OnClosing(e);
        System.Windows.Application.Current.Shutdown();
    }
}
