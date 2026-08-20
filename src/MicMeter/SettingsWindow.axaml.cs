using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using MicMeter.Models;
using MicMeter.Services;

namespace MicMeter;

public partial class SettingsWindow : Window
{
    // AUHAL buffer frame-size options (0 = hardware default). Matches the order
    // of items in the BufferSizeComboBox declared in the AXAML.
    private static readonly int[] BufferSizeValues = [0, 128, 256, 512, 1024];

    private readonly AppSettings _settings;
    private readonly Action _quitAction;
    private readonly ObservableCollection<DeviceSelectionItem> _deviceItems;
    private readonly IReadOnlyList<AudioDeviceInfo> _outputDevices;
    private bool _initializing = true;
    private bool _capturingHotkey;
    private uint _pendingHotkeyModifiers;
    private int _pendingHotkeyVirtualKey;
    private string _lowColor;
    private string _midColor;
    private string _highColor;

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    public event EventHandler? MuteOverlayPlacementRequested;

    public SettingsWindow(AppSettings settings, IReadOnlyList<AudioDeviceInfo> devices, IReadOnlyList<AudioDeviceInfo> outputDevices, Action quitAction)
    {
        InitializeComponent();
        _settings = settings;
        _quitAction = quitAction;
        _outputDevices = outputDevices;
        settings.Migrate();
        _pendingHotkeyModifiers = settings.MuteHotkeyModifiers;
        _pendingHotkeyVirtualKey = settings.MuteHotkeyVirtualKey;
        _lowColor = settings.LowLevelColor;
        _midColor = settings.MidLevelColor;
        _highColor = settings.HighLevelColor;

        var deviceById = devices.ToDictionary(device => device.Id);
        var orderedDevices = settings.DeviceIds
            .Where(deviceById.ContainsKey)
            .Select(id => deviceById[id])
            .Concat(devices.Where(device => !settings.DeviceIds.Contains(device.Id)));
        _deviceItems = new ObservableCollection<DeviceSelectionItem>(orderedDevices.Select(device =>
            new DeviceSelectionItem(
                device,
                settings.DeviceIds.Contains(device.Id),
                settings.LoopbackDeviceIds.Contains(device.Id))));
        if (_deviceItems.All(item => !item.IsSelected) && _deviceItems.Count > 0)
        {
            _deviceItems[0].IsSelected = true;
        }

        DeviceListBox.ItemsSource = _deviceItems;
        PlacementComboBox.ItemsSource = GetPlacements(settings.UiLanguage == AppLanguage.English);
        PlacementComboBox.SelectedItem = GetPlacements(settings.UiLanguage == AppLanguage.English)
            .First(option => option.Value == settings.Placement);
        ScaleSlider.Value = settings.Scale;
        OpacitySlider.Value = settings.Opacity;
        TopmostCheckBox.IsChecked = settings.Topmost;
        StartWithWindowsCheckBox.IsChecked = StartupService.IsEnabled();
        settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        UiLanguageComboBox.SelectedIndex = settings.UiLanguage == AppLanguage.English ? 1 : 0;
        UpdateHotkeyButton();
        OrientationComboBox.SelectedIndex = settings.DisplayOrientation == MeterDisplayOrientation.Vertical ? 1 : 0;
        ThemeComboBox.SelectedIndex = settings.Theme == AppTheme.FlatBlack ? 1 : 0;
        ShowDeviceNameCheckBox.IsChecked = settings.ShowDeviceName;
        ShowLevelTextCheckBox.IsChecked = settings.ShowLevelText;
        ShowStatusTextCheckBox.IsChecked = settings.ShowStatusText;
        ShowMuteControlCheckBox.IsChecked = settings.ShowMuteControl;
        ShowListeningControlCheckBox.IsChecked = settings.ShowListeningControl;
        ShowPeakHoldCheckBox.IsChecked = settings.ShowPeakHold;
        ShowClippingWarningCheckBox.IsChecked = settings.ShowClippingWarning;
        ShowTrayMeterCheckBox.IsChecked = settings.ShowTrayMeter;
        PlayMuteSoundsCheckBox.IsChecked = settings.PlayMuteSounds;
        ShowMuteOverlayCheckBox.IsChecked = settings.ShowMuteOverlay;
        MidThresholdTextBox.Text = settings.MidLevelThresholdDb.ToString("0.##", CultureInfo.CurrentCulture);
        HighThresholdTextBox.Text = settings.HighLevelThresholdDb.ToString("0.##", CultureInfo.CurrentCulture);
        UpdateColorButtons();

        SegmentCountComboBox.Items.Clear();
        foreach (var value in Enumerable.Range(8, 33))
        {
            var item = new ComboBoxItem { Content = value.ToString() };
            SegmentCountComboBox.Items.Add(item);
            if (value == settings.SegmentCount)
            {
                SegmentCountComboBox.SelectedItem = item;
            }
        }

        if (SegmentCountComboBox.SelectedItem is null && SegmentCountComboBox.Items.Count > 0)
        {
            SegmentCountComboBox.SelectedIndex = 0;
        }

        // Buffer size and routing mode are macOS-only. Hide on Windows.
        if (!OperatingSystem.IsMacOS())
        {
            BufferSizeLabel.IsVisible = false;
            BufferSizeComboBox.IsVisible = false;
            RoutingModeLabel.IsVisible = false;
            RoutingModeComboBox.IsVisible = false;
        }
        else
        {
            var bufferSizeIndex = Array.IndexOf(BufferSizeValues, settings.BufferFrameSize);
            BufferSizeComboBox.SelectedIndex = bufferSizeIndex >= 0 ? bufferSizeIndex : 0;
            RoutingModeComboBox.SelectedIndex = settings.RoutingMode == AudioRoutingMode.Plugin ? 1 : 0;
        }

        RefreshListeningOutputItems();

        ApplyLanguage();
        WireImmediateApplyEvents();
        _initializing = false;
    }

    private void ApplyChanges()
    {
        var selectedDevices = _deviceItems.Where(item => item.IsSelected).ToArray();
        if (selectedDevices.Length == 0)
        {
            return;
        }

        if (!TryParseDb(MidThresholdTextBox.Text, out var midThreshold) ||
            !TryParseDb(HighThresholdTextBox.Text, out var highThreshold) ||
            midThreshold < LevelMath.MinimumDb || highThreshold > 0 || midThreshold >= highThreshold)
        {
            return;
        }

        var selectedDeviceIds = selectedDevices.Select(device => device.Id).ToList();
        var loopbackDeviceIds = selectedDevices
            .Where(device => device.IsLoopback)
            .Select(device => device.Id)
            .ToList();
        var bufferFrameSize = BufferSizeComboBox.SelectedIndex is int bufferIndex && bufferIndex >= 0 && bufferIndex < BufferSizeValues.Length
            ? BufferSizeValues[bufferIndex]
            : 0;
        var routingMode = RoutingModeComboBox.SelectedIndex == 1
            ? AudioRoutingMode.Plugin
            : AudioRoutingMode.Loopback;
        var listeningOutputDeviceId = ListeningOutputComboBox.SelectedItem is OutputDeviceOption outputOption
            ? outputOption.Value
            : null;
        var requiresReconnect = !_settings.DeviceIds.SequenceEqual(selectedDeviceIds) ||
                                !_settings.LoopbackDeviceIds.SequenceEqual(loopbackDeviceIds) ||
                                _settings.BufferFrameSize != bufferFrameSize ||
                                _settings.RoutingMode != routingMode ||
                                _settings.ListeningOutputDeviceId != listeningOutputDeviceId;
        var resetWindowSize = Math.Abs(_settings.Scale - ScaleSlider.Value) > 0.001 ||
                              _settings.DisplayOrientation != (OrientationComboBox.SelectedIndex == 1
                                  ? MeterDisplayOrientation.Vertical
                                  : MeterDisplayOrientation.Horizontal) || requiresReconnect;
        _settings.DeviceIds = selectedDeviceIds;
        _settings.LoopbackDeviceIds = loopbackDeviceIds;
        _settings.BufferFrameSize = bufferFrameSize;
        _settings.RoutingMode = routingMode;
        _settings.ListeningOutputDeviceId = listeningOutputDeviceId;
        _settings.DeviceId = null;
        _settings.Scale = ScaleSlider.Value;
        _settings.Opacity = OpacitySlider.Value;
        _settings.Topmost = TopmostCheckBox.IsChecked == true;
        var startWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        if (_settings.StartWithWindows != startWithWindows || StartupService.IsEnabled() != startWithWindows)
        {
            StartupService.SetEnabled(startWithWindows);
        }

        _settings.StartWithWindows = startWithWindows;
        _settings.UiLanguage = IsEnglish ? AppLanguage.English : AppLanguage.Japanese;
        _settings.MuteHotkeyModifiers = _pendingHotkeyModifiers;
        _settings.MuteHotkeyVirtualKey = _pendingHotkeyVirtualKey;
        _settings.MuteHotkeyEnabled = _pendingHotkeyVirtualKey != 0;
        _settings.DisplayOrientation = OrientationComboBox.SelectedIndex == 1
            ? MeterDisplayOrientation.Vertical
            : MeterDisplayOrientation.Horizontal;
        _settings.Theme = ThemeComboBox.SelectedIndex == 1 ? AppTheme.FlatBlack : AppTheme.MidnightGlass;
        _settings.ShowDeviceName = ShowDeviceNameCheckBox.IsChecked == true;
        _settings.ShowLevelText = ShowLevelTextCheckBox.IsChecked == true;
        _settings.ShowStatusText = ShowStatusTextCheckBox.IsChecked == true;
        _settings.ShowMuteControl = ShowMuteControlCheckBox.IsChecked == true;
        _settings.ShowListeningControl = ShowListeningControlCheckBox.IsChecked == true;
        _settings.ShowPeakHold = ShowPeakHoldCheckBox.IsChecked == true;
        _settings.ShowClippingWarning = ShowClippingWarningCheckBox.IsChecked == true;
        _settings.ShowTrayMeter = ShowTrayMeterCheckBox.IsChecked == true;
        _settings.PlayMuteSounds = PlayMuteSoundsCheckBox.IsChecked == true;
        _settings.ShowMuteOverlay = ShowMuteOverlayCheckBox.IsChecked == true;
        _settings.LowLevelColor = _lowColor;
        _settings.MidLevelColor = _midColor;
        _settings.HighLevelColor = _highColor;
        _settings.MidLevelThresholdDb = midThreshold;
        _settings.HighLevelThresholdDb = highThreshold;
        _settings.Placement = PlacementComboBox.SelectedItem is PlacementOption placement
            ? placement.Value
            : OverlayPlacement.BottomRight;

        if (SegmentCountComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var segmentCount))
        {
            _settings.SegmentCount = segmentCount;
        }

        if (resetWindowSize)
        {
            _settings.WindowWidth = null;
            _settings.WindowHeight = null;
        }

        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(requiresReconnect));
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void QuitButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
        _quitAction();
    }

    private void WireImmediateApplyEvents()
    {
        ScaleSlider.ValueChanged += (_, _) => ApplyChangesIfReady();
        OpacitySlider.ValueChanged += (_, _) => ApplyChangesIfReady();
        PlacementComboBox.SelectionChanged += (_, _) => ApplyChangesIfReady();
        SegmentCountComboBox.SelectionChanged += (_, _) => ApplyChangesIfReady();
        BufferSizeComboBox.SelectionChanged += (_, _) => ApplyChangesIfReady();
        RoutingModeComboBox.SelectionChanged += (_, _) => ApplyChangesIfReady();
        ListeningOutputComboBox.SelectionChanged += (_, _) => ApplyChangesIfReady();
        OrientationComboBox.SelectionChanged += (_, _) => ApplyChangesIfReady();
        ThemeComboBox.SelectionChanged += (_, _) => ApplyChangesIfReady();
        UiLanguageComboBox.SelectionChanged += UiLanguageComboBox_SelectionChanged;

        var checkBoxes = new[]
        {
            TopmostCheckBox, StartWithWindowsCheckBox, ShowDeviceNameCheckBox, ShowLevelTextCheckBox,
            ShowStatusTextCheckBox, ShowMuteControlCheckBox, ShowListeningControlCheckBox,
            ShowPeakHoldCheckBox, ShowClippingWarningCheckBox, ShowTrayMeterCheckBox,
            PlayMuteSoundsCheckBox, ShowMuteOverlayCheckBox
        };
        foreach (var checkBox in checkBoxes)
        {
            checkBox.IsCheckedChanged += (_, _) => ApplyChangesIfReady();
        }

        MidThresholdTextBox.LostFocus += (_, _) => ApplyChangesIfReady();
        HighThresholdTextBox.LostFocus += (_, _) => ApplyChangesIfReady();
        foreach (var item in _deviceItems)
        {
            item.PropertyChanged += DeviceItem_PropertyChanged;
        }
    }

    private void DeviceItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceSelectionItem.IsSelected) &&
            _deviceItems.All(item => !item.IsSelected) && sender is DeviceSelectionItem changedItem)
        {
            changedItem.IsSelected = true;
            return;
        }

        ApplyChangesIfReady();
    }

    private void ApplyChangesIfReady()
    {
        if (!_initializing)
        {
            ApplyChanges();
        }
    }

    private void MuteOverlayPlacementButton_Click(object? sender, RoutedEventArgs e) =>
        MuteOverlayPlacementRequested?.Invoke(this, EventArgs.Empty);

    private async void ColorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string target)
        {
            return;
        }

        var current = target switch
        {
            "Low" => _lowColor,
            "Mid" => _midColor,
            _ => _highColor
        };
        var currentColor = ParseColor(current);
        var selected = await PickColorAsync(currentColor);
        if (selected is null)
        {
            return;
        }

        var text = $"#{selected.Value.R:X2}{selected.Value.G:X2}{selected.Value.B:X2}";
        if (target == "Low") _lowColor = text;
        else if (target == "Mid") _midColor = text;
        else _highColor = text;
        UpdateColorButtons();
        ApplyChangesIfReady();
    }

    private async Task<Color?> PickColorAsync(Color current)
    {
        var colorView = new ColorView { Color = current };
        var okButton = new Button { Content = "OK", MinWidth = 76 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 76 };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin = new Thickness(12)
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(colorView);
        grid.Children.Add(buttons);
        Grid.SetRow(colorView, 0);
        Grid.SetRow(buttons, 1);

        var dialog = new Window
        {
            Title = "色の選択 / Pick color",
            Width = 440,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = grid
        };
        Color? result = null;
        okButton.Click += (_, _) => { result = colorView.Color; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return result;
    }

    private void UpdateColorButtons()
    {
        SetColorButton(LowColorButton, _lowColor);
        SetColorButton(MidColorButton, _midColor);
        SetColorButton(HighColorButton, _highColor);
    }

    private static void SetColorButton(Button button, string colorText)
    {
        var color = ParseColor(colorText);
        button.Background = new SolidColorBrush(color);
        button.Content = colorText;
        button.Foreground = Brushes.White;
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return Color.Parse(value);
        }
        catch
        {
            return Colors.Gray;
        }
    }

    private static bool TryParseDb(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void HotkeyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
        {
            _pendingHotkeyModifiers = 0;
            _pendingHotkeyVirtualKey = 0;
            _capturingHotkey = false;
            UpdateHotkeyButton();
            ApplyChangesIfReady();
            return;
        }

        _capturingHotkey = true;
        HotkeyButton.Content = T("キーを入力…（再クリックで解除）", "Press a key… (click again to clear)");
        HotkeyButton.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_capturingHotkey)
        {
            return;
        }

        var key = e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        _pendingHotkeyModifiers = ToHotkeyModifiers(e.KeyModifiers);
        _pendingHotkeyVirtualKey = KeyToVirtualKey(key);
        _capturingHotkey = false;
        UpdateHotkeyButton();
        ApplyChangesIfReady();
        e.Handled = true;
    }

    private void UpdateHotkeyButton() =>
        HotkeyButton.Content = HotkeyFormat.Format(_pendingHotkeyModifiers, _pendingHotkeyVirtualKey);

    private static uint ToHotkeyModifiers(KeyModifiers modifiers)
    {
        uint value = 0;
        if (modifiers.HasFlag(KeyModifiers.Alt)) value |= 0x0001;
        if (modifiers.HasFlag(KeyModifiers.Control)) value |= 0x0002;
        if (modifiers.HasFlag(KeyModifiers.Shift)) value |= 0x0004;
        if (modifiers.HasFlag(KeyModifiers.Meta)) value |= 0x0008;
        return value;
    }

    private static int KeyToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return 'A' + (key - Key.A);
        if (key >= Key.D0 && key <= Key.D9) return '0' + (key - Key.D0);
        if (key >= Key.F1 && key <= Key.F12) return 0x70 + (key - Key.F1);
        return key switch
        {
            Key.Return => 0x0D,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.Back => 0x08,
            Key.Tab => 0x09,
            _ => 0
        };
    }

    private void MoveUpButton_Click(object? sender, RoutedEventArgs e) => MoveSelectedDevice(-1);
    private void MoveDownButton_Click(object? sender, RoutedEventArgs e) => MoveSelectedDevice(1);

    private void MoveSelectedDevice(int offset)
    {
        if (DeviceListBox.SelectedItem is not DeviceSelectionItem item)
        {
            return;
        }

        var oldIndex = _deviceItems.IndexOf(item);
        var newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= _deviceItems.Count)
        {
            return;
        }

        _deviceItems.Move(oldIndex, newIndex);
        DeviceListBox.SelectedItem = item;
        DeviceListBox.ScrollIntoView(item);
        ApplyChangesIfReady();
    }

    private bool IsEnglish => UiLanguageComboBox.SelectedIndex == 1;

    private static PlacementOption[] GetPlacements(bool english) => english
        ?
        [
            new("Bottom right", OverlayPlacement.BottomRight),
            new("Bottom center", OverlayPlacement.BottomCenter),
            new("Bottom left", OverlayPlacement.BottomLeft),
            new("Top right", OverlayPlacement.TopRight),
            new("Top center", OverlayPlacement.TopCenter),
            new("Top left", OverlayPlacement.TopLeft),
            new("Custom", OverlayPlacement.Custom)
        ]
        :
        [
            new("右下", OverlayPlacement.BottomRight),
            new("下中央", OverlayPlacement.BottomCenter),
            new("左下", OverlayPlacement.BottomLeft),
            new("右上", OverlayPlacement.TopRight),
            new("上中央", OverlayPlacement.TopCenter),
            new("左上", OverlayPlacement.TopLeft),
            new("自由配置", OverlayPlacement.Custom)
        ];

    private void UiLanguageComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyLanguage();
        ApplyChangesIfReady();
    }

    private void ApplyLanguage()
    {
        Title = IsEnglish ? "MicMeter Settings" : "MicMeter 設定";
        TranslateTree(this);

        var selectedPlacement = PlacementComboBox.SelectedItem is PlacementOption placement
            ? placement.Value
            : _settings.Placement;
        var placements = GetPlacements(IsEnglish);
        PlacementComboBox.ItemsSource = placements;
        PlacementComboBox.SelectedItem = placements.First(option => option.Value == selectedPlacement);

        SetComboItems(OrientationComboBox, [T("横型", "Horizontal"), T("縦型", "Vertical")]);
        OrientationComboBox.SelectedIndex = _settings.DisplayOrientation == MeterDisplayOrientation.Vertical ? 1 : 0;
        SetComboItems(ThemeComboBox, ["Midnight Glass", T("Flat Black（角丸なし）", "Flat Black (square corners)")]);
        ThemeComboBox.SelectedIndex = _settings.Theme == AppTheme.FlatBlack ? 1 : 0;
        RefreshListeningOutputItems();
        UpdateHotkeyButton();
        UpdateTrayMeterLabel();
    }

    private void RefreshListeningOutputItems()
    {
        var selectedValue = ListeningOutputComboBox.SelectedItem is OutputDeviceOption current
            ? current.Value
            : _settings.ListeningOutputDeviceId;
        var options = new List<OutputDeviceOption>
        {
            new(T("システムのデフォルト", "System default"), null)
        };
        options.AddRange(_outputDevices.Select(device => new OutputDeviceOption(device.Name, device.Id)));
        ListeningOutputComboBox.ItemsSource = options;
        ListeningOutputComboBox.SelectedItem = options.FirstOrDefault(option => option.Value == selectedValue) ?? options[0];
    }

    private void UpdateTrayMeterLabel()
    {
        ShowTrayMeterCheckBox.Content = OperatingSystem.IsMacOS()
            ? T("メニューバーメーター", "Menu bar meter")
            : T("トレイメーター", "Tray meter");
    }

    private static void SetComboItems(ComboBox combo, string[] items)
    {
        var index = combo.SelectedIndex;
        combo.Items.Clear();
        foreach (var item in items)
        {
            combo.Items.Add(new ComboBoxItem { Content = item });
        }

        combo.SelectedIndex = index;
    }

    private void TranslateTree(Control parent)
    {
        foreach (var child in LogicalExtensions.GetLogicalChildren(parent))
        {
            if (child is TextBlock textBlock)
            {
                textBlock.Text = LocalizationStrings.Translate(textBlock.Text ?? string.Empty, IsEnglish);
            }
            else if (child is ContentControl contentControl && contentControl.Content is string content)
            {
                contentControl.Content = LocalizationStrings.Translate(content, IsEnglish);
            }

            if (child is Control control)
            {
                TranslateTree(control);
            }
        }
    }

    private string T(string japanese, string english) => IsEnglish ? english : japanese;
}

public sealed record PlacementOption(string Label, OverlayPlacement Value);

public sealed record OutputDeviceOption(string Label, string? Value);

public sealed class SettingsChangedEventArgs(bool requiresReconnect) : EventArgs
{
    public bool RequiresReconnect { get; } = requiresReconnect;
}
