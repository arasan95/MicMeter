using System.Windows;
using System.Windows.Input;
using MicMeter.Models;
using MicMeter.Services;

namespace MicMeter;

public partial class MuteNotificationWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;

    public MuteNotificationWindow(AppSettings settings, SettingsStore settingsStore)
    {
        InitializeComponent();
        _settings = settings;
        _settingsStore = settingsStore;
        Loaded += (_, _) => ApplySavedPlacement();
    }

    public bool IsPlacementPreview { get; private set; }

    public void ShowMuted(string? deviceName, bool allMuted)
    {
        if (IsPlacementPreview)
        {
            return;
        }

        TitleText.Text = T("マイク ミュート中", "MIC MUTED");
        DetailText.Text = allMuted
            ? T("すべての入力デバイスがミュートされています", "All input devices are muted")
            : string.IsNullOrWhiteSpace(deviceName)
                ? T("入力デバイスがミュートされています", "An input device is muted")
                : deviceName;
        Shell.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(255, 73, 97));
        ShowWithoutActivation();
    }

    public void ShowPlacementPreview()
    {
        IsPlacementPreview = true;
        TitleText.Text = T("ここへドラッグ", "DRAG TO POSITION");
        DetailText.Text = T("この表示を好きな場所へ移動してください", "Move this overlay to the desired location");
        Shell.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(86, 166, 255));
        ShowWithoutActivation();
    }

    public void EndPlacementPreview()
    {
        IsPlacementPreview = false;
        Hide();
    }

    public void HideStatus()
    {
        if (!IsPlacementPreview)
        {
            Hide();
        }
    }

    private void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            Show();
        }

        Topmost = false;
        Topmost = true;
    }

    private void ApplySavedPlacement()
    {
        var area = SystemParameters.WorkArea;
        Left = _settings.MuteOverlayLeft ?? area.Left + ((area.Width - ActualWidth) / 2);
        Top = _settings.MuteOverlayTop ?? area.Top + 36;
        ClampToWorkArea();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            ClampToWorkArea();
            _settings.MuteOverlayLeft = Left;
            _settings.MuteOverlayTop = Top;
            _settingsStore.Save(_settings);
        }
        catch (InvalidOperationException)
        {
            // The mouse may be released before DragMove starts.
        }
    }

    private void ClampToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }

    private string T(string japanese, string english) =>
        _settings.UiLanguage == AppLanguage.English ? english : japanese;
}
