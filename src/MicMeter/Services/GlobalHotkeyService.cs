using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace MicMeter.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4D4D;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private HwndSource? _source;
    private nint _windowHandle;
    private bool _registered;

    public event EventHandler? Pressed;

    public bool Register(Window window, uint modifiers, int virtualKey)
    {
        Unregister();
        if (virtualKey == 0)
        {
            return false;
        }
        _windowHandle = new WindowInteropHelper(window).Handle;
        if (_windowHandle == 0)
        {
            return false;
        }

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowHook);
        _registered = RegisterHotKey(_windowHandle, HotkeyId, modifiers | ModNoRepeat, (uint)virtualKey);
        if (!_registered)
        {
            _source?.RemoveHook(WindowHook);
            _source = null;
        }

        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
        }

        _source?.RemoveHook(WindowHook);
        _source = null;
        _windowHandle = 0;
        _registered = false;
    }

    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return 0;
    }

    public void Dispose() => Unregister();

    public static string Format(uint modifiers, int virtualKey)
    {
        if (virtualKey == 0)
        {
            return "未設定";
        }

        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        parts.Add(key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.Back => "Backspace",
            _ => key.ToString()
        });
        return string.Join(" + ", parts);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
