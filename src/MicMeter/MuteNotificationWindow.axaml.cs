using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
        Opened += (_, _) => ApplySavedPlacement();
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
        Shell.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 73, 97));
        ShowWithoutActivation();
    }

    public void ShowPlacementPreview()
    {
        IsPlacementPreview = true;
        TitleText.Text = T("ここへドラッグ", "DRAG TO POSITION");
        DetailText.Text = T("この表示を好きな場所へ移動してください", "Move this overlay to the desired location");
        Shell.BorderBrush = new SolidColorBrush(Color.FromRgb(86, 166, 255));
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
        var area = GetWorkingArea();
        var left = _settings.MuteOverlayLeft ?? area.X + ((area.Width - Width) / 2);
        var top = _settings.MuteOverlayTop ?? area.Y + 36;
        SetPosition(left, top);
        ClampToWorkArea();
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginMoveDrag(e);
        ClampToWorkArea();
        _settings.MuteOverlayLeft = GetLeft();
        _settings.MuteOverlayTop = GetTop();
        _settingsStore.Save(_settings);
    }

    private void ClampToWorkArea()
    {
        var area = GetWorkingArea();
        var left = Math.Clamp(GetLeft(), area.X, Math.Max(area.X, area.Right - Width));
        var top = Math.Clamp(GetTop(), area.Y, Math.Max(area.Y, area.Bottom - Height));
        SetPosition(left, top);
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

    private string T(string japanese, string english) =>
        _settings.UiLanguage == AppLanguage.English ? english : japanese;
}
