using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MicMeter.Models;
using MicMeter.Services;

namespace MicMeter;

public partial class SettingsWindow : Window
{
    private static readonly IReadOnlyDictionary<string, string> EnglishLabels =
        new Dictionary<string, string>
        {
            ["MicMeter 設定"] = "MicMeter Settings",
            ["入力デバイス"] = "Input devices",
            ["↑ 上へ"] = "↑ Up",
            ["↓ 下へ"] = "↓ Down",
            ["一覧の上から順に表示されます。項目を選び、上下ボタンで表示順を変更できます。"] =
                "Devices are shown in this order. Select an item and use Up or Down to reorder it.",
            ["大きさ"] = "Scale",
            ["透明度"] = "Opacity",
            ["表示位置"] = "Placement",
            ["セグメント数"] = "Segments",
            ["メーター方向"] = "Meter orientation",
            ["横型"] = "Horizontal",
            ["縦型"] = "Vertical",
            ["テーマ"] = "Theme",
            ["Flat Black（角丸なし）"] = "Flat Black (square corners)",
            ["言語"] = "Language",
            ["常に手前に表示"] = "Always on top",
            ["全ミュート・ホットキー"] = "Mute-all hotkey",
            ["表示項目"] = "Visible elements",
            ["デバイス名"] = "Device name",
            ["dB数値"] = "dB value",
            ["状態文字"] = "Status text",
            ["ミュート操作"] = "Mute control",
            ["リスニング操作"] = "Listening control",
            ["ピークホールド"] = "Peak hold",
            ["CLIP警告"] = "Clipping warning",
            ["トレイメーター"] = "Tray meter",
            ["ミュート通知音"] = "Mute/unmute sounds",
            ["ミュート表示"] = "Mute overlay",
            ["ミュート表示の位置を調整"] = "Position mute overlay",
            ["メーターバーの色と切替位置"] = "Meter colors and thresholds",
            ["低レベル"] = "Low level",
            ["中レベル"] = "Mid level",
            ["高レベル"] = "High level",
            ["中色へ切替 (dB)"] = "Mid threshold (dB)",
            ["高色へ切替 (dB)"] = "High threshold (dB)",
            ["細くリサイズすると文字は自動で隠れ、ミュートとリスニングは点表示になります。耳ボタンはヘッドホン使用を推奨します。"] =
                "Text is hidden automatically at compact sizes; mute and listening become status dots. Headphones are recommended when listening.",
            ["キャンセル"] = "Cancel",
            ["保存"] = "Save"
        };

    private readonly AppSettings _settings;
    private readonly ObservableCollection<DeviceSelectionItem> _deviceItems;
    private readonly double _initialScale;
    private readonly MeterDisplayOrientation _initialOrientation;
    private readonly int _initialDeviceCount;
    private bool _capturingHotkey;
    private uint _pendingHotkeyModifiers;
    private int _pendingHotkeyVirtualKey;
    private string _lowColor;
    private string _midColor;
    private string _highColor;

    public event EventHandler? SettingsSaved;
    public event EventHandler? MuteOverlayPlacementRequested;

    public SettingsWindow(AppSettings settings, IReadOnlyList<AudioDeviceInfo> devices)
    {
        InitializeComponent();
        _settings = settings;
        settings.Migrate();
        _initialScale = settings.Scale;
        _initialOrientation = settings.DisplayOrientation;
        _initialDeviceCount = settings.DeviceIds.Count;
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
            new DeviceSelectionItem(device, settings.DeviceIds.Contains(device.Id))));
        if (_deviceItems.All(item => !item.IsSelected) && _deviceItems.Count > 0)
        {
            _deviceItems[0].IsSelected = true;
        }
        DeviceListBox.ItemsSource = _deviceItems;

        PlacementComboBox.ItemsSource = GetPlacements(settings.UiLanguage == AppLanguage.English);
        PlacementComboBox.DisplayMemberPath = "Item1";
        PlacementComboBox.SelectedValuePath = "Item2";
        PlacementComboBox.SelectedValue = settings.Placement;
        ScaleSlider.Value = settings.Scale;
        OpacitySlider.Value = settings.Opacity;
        TopmostCheckBox.IsChecked = settings.Topmost;
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

        foreach (ComboBoxItem item in SegmentCountComboBox.Items)
        {
            if (item.Content?.ToString() == settings.SegmentCount.ToString())
            {
                SegmentCountComboBox.SelectedItem = item;
                break;
            }
        }

        SegmentCountComboBox.SelectedIndex = Math.Max(0, SegmentCountComboBox.SelectedIndex);
        ApplyLanguage();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedDevices = _deviceItems.Where(item => item.IsSelected).ToArray();
        if (selectedDevices.Length == 0)
        {
            System.Windows.MessageBox.Show(this,
                T("表示する入力デバイスを1つ以上選択してください。", "Select at least one input device."), "MicMeter",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryParseDb(MidThresholdTextBox.Text, out var midThreshold) ||
            !TryParseDb(HighThresholdTextBox.Text, out var highThreshold) ||
            midThreshold < LevelMath.MinimumDb || highThreshold > 0 || midThreshold >= highThreshold)
        {
            System.Windows.MessageBox.Show(this,
                T("切替位置は -60～0 dB の範囲で、中レベルを高レベルより小さく設定してください。",
                    "Thresholds must be between -60 and 0 dB, and the mid threshold must be lower than the high threshold."),
                "MicMeter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _settings.DeviceIds = selectedDevices.Select(device => device.Id).ToList();
        _settings.DeviceId = null;
        _settings.Scale = ScaleSlider.Value;
        _settings.Opacity = OpacitySlider.Value;
        _settings.Topmost = TopmostCheckBox.IsChecked == true;
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
        _settings.Placement = PlacementComboBox.SelectedValue is OverlayPlacement placement
            ? placement
            : OverlayPlacement.BottomRight;

        if (SegmentCountComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var segmentCount))
        {
            _settings.SegmentCount = segmentCount;
        }

        if (Math.Abs(_initialScale - _settings.Scale) > 0.001 ||
            _initialOrientation != _settings.DisplayOrientation ||
            _initialDeviceCount != _settings.DeviceIds.Count)
        {
            _settings.WindowWidth = null;
            _settings.WindowHeight = null;
        }

        SettingsSaved?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MuteOverlayPlacementButton_Click(object sender, RoutedEventArgs e) =>
        MuteOverlayPlacementRequested?.Invoke(this, EventArgs.Empty);

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string target)
        {
            return;
        }

        var current = target switch
        {
            "Low" => _lowColor,
            "Mid" => _midColor,
            _ => _highColor
        };
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = System.Drawing.ColorTranslator.FromHtml(current)
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        var selected = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        if (target == "Low") _lowColor = selected;
        else if (target == "Mid") _midColor = selected;
        else _highColor = selected;
        UpdateColorButtons();
    }

    private void UpdateColorButtons()
    {
        SetColorButton(LowColorButton, _lowColor);
        SetColorButton(MidColorButton, _midColor);
        SetColorButton(HighColorButton, _highColor);
    }

    private static void SetColorButton(System.Windows.Controls.Button button, string colorText)
    {
        try
        {
            button.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorText)!);
        }
        catch
        {
            button.Background = System.Windows.Media.Brushes.Gray;
        }
        button.Content = colorText;
    }

    private static bool TryParseDb(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
        {
            _pendingHotkeyModifiers = 0;
            _pendingHotkeyVirtualKey = 0;
            _capturingHotkey = false;
            UpdateHotkeyButton();
            return;
        }

        _capturingHotkey = true;
        HotkeyButton.Content = T("キーを入力…（再クリックで解除）", "Press a key… (click again to clear)");
        HotkeyButton.Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotkey)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        _pendingHotkeyModifiers = ToHotkeyModifiers(Keyboard.Modifiers);
        _pendingHotkeyVirtualKey = KeyInterop.VirtualKeyFromKey(key);
        _capturingHotkey = false;
        UpdateHotkeyButton();
        e.Handled = true;
    }

    private void UpdateHotkeyButton()
    {
        HotkeyButton.Content = GlobalHotkeyService.Format(_pendingHotkeyModifiers, _pendingHotkeyVirtualKey);
    }

    private static uint ToHotkeyModifiers(ModifierKeys modifiers)
    {
        uint value = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) value |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) value |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) value |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) value |= 0x0008;
        return value;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelectedDevice(-1);
    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelectedDevice(1);

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
    }

    private bool IsEnglish => UiLanguageComboBox.SelectedIndex == 1;

    private static (string Label, OverlayPlacement Value)[] GetPlacements(bool english) => english
        ?
        [
            ("Bottom right", OverlayPlacement.BottomRight),
            ("Bottom center", OverlayPlacement.BottomCenter),
            ("Bottom left", OverlayPlacement.BottomLeft),
            ("Top right", OverlayPlacement.TopRight),
            ("Top center", OverlayPlacement.TopCenter),
            ("Top left", OverlayPlacement.TopLeft),
            ("Custom", OverlayPlacement.Custom)
        ]
        :
        [
            ("右下", OverlayPlacement.BottomRight),
            ("下中央", OverlayPlacement.BottomCenter),
            ("左下", OverlayPlacement.BottomLeft),
            ("右上", OverlayPlacement.TopRight),
            ("上中央", OverlayPlacement.TopCenter),
            ("左上", OverlayPlacement.TopLeft),
            ("自由配置", OverlayPlacement.Custom)
        ];

    private void UiLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        Title = IsEnglish ? "MicMeter Settings" : "MicMeter 設定";
        TranslateTree(this);
        var selectedPlacement = PlacementComboBox.SelectedValue is OverlayPlacement placement
            ? placement
            : _settings.Placement;
        PlacementComboBox.ItemsSource = GetPlacements(IsEnglish);
        PlacementComboBox.DisplayMemberPath = "Item1";
        PlacementComboBox.SelectedValuePath = "Item2";
        PlacementComboBox.SelectedValue = selectedPlacement;
        UpdateHotkeyButton();
    }

    private void TranslateTree(DependencyObject parent)
    {
        var reverseLabels = EnglishLabels.ToDictionary(pair => pair.Value, pair => pair.Key);
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is TextBlock textBlock)
            {
                textBlock.Text = TranslateLabel(textBlock.Text, reverseLabels);
            }
            else if (child is ContentControl contentControl && contentControl.Content is string content)
            {
                contentControl.Content = TranslateLabel(content, reverseLabels);
            }

            TranslateTree(child);
        }
    }

    private string TranslateLabel(string value, IReadOnlyDictionary<string, string> reverseLabels)
    {
        if (IsEnglish)
        {
            return EnglishLabels.TryGetValue(value, out var englishLabel) ? englishLabel : value;
        }

        return reverseLabels.TryGetValue(value, out var japaneseLabel) ? japaneseLabel : value;
    }

    private string T(string japanese, string english) => IsEnglish ? english : japanese;
}
