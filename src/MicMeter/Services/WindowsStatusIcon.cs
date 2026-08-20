using Avalonia.Controls;

namespace MicMeter.Services;

public sealed class WindowsStatusIcon : IStatusIcon
{
    private readonly TrayIcon _trayIcon;
    private readonly StatusIconActions _actions;
    private readonly Func<string, string, string> _translate;
    private NativeMenuItem? _showItem;
    private NativeMenuItem? _muteItem;
    private NativeMenuItem? _settingsItem;
    private NativeMenuItem? _exitItem;

    public WindowsStatusIcon(byte[] iconPng, StatusIconActions actions, Func<string, string, string> translate)
    {
        _actions = actions;
        _translate = translate;
        _trayIcon = new TrayIcon
        {
            Icon = ToWindowIcon(iconPng),
            ToolTipText = "MicMeter",
            IsVisible = true
        };
        _trayIcon.Menu = BuildMenu();
        _trayIcon.Clicked += (_, _) => LeftClicked?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? LeftClicked;

    public void SetIcon(byte[] pngBytes) => _trayIcon.Icon = ToWindowIcon(pngBytes);

    public void SetToolTip(string text) => _trayIcon.ToolTipText = text;

    public void UpdateLanguage()
    {
        if (_showItem is not null) _showItem.Header = _translate("表示 / 非表示", "Show / Hide");
        if (_muteItem is not null) _muteItem.Header = _translate("すべてミュート切り替え", "Toggle mute all");
        if (_settingsItem is not null) _settingsItem.Header = _translate("設定", "Settings");
        if (_exitItem is not null) _exitItem.Header = _translate("終了", "Quit");
    }

    public void Dispose()
    {
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        _showItem = new NativeMenuItem
        {
            Header = _translate("表示 / 非表示", "Show / Hide"),
            Command = new RelayCommand(_actions.ShowHide)
        };
        _muteItem = new NativeMenuItem
        {
            Header = _translate("すべてミュート切り替え", "Toggle mute all"),
            Command = new RelayCommand(_actions.ToggleMute)
        };
        _settingsItem = new NativeMenuItem
        {
            Header = _translate("設定", "Settings"),
            Command = new RelayCommand(_actions.Settings)
        };
        _exitItem = new NativeMenuItem
        {
            Header = _translate("終了", "Quit"),
            Command = new RelayCommand(_actions.Quit)
        };
        menu.Items.Add(_showItem);
        menu.Items.Add(_muteItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_exitItem);
        return menu;
    }

    private static WindowIcon ToWindowIcon(byte[] png) => new(new MemoryStream(png));
}
