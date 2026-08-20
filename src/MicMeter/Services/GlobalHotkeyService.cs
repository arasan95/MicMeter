using System.Runtime.InteropServices;
using System.Threading;

namespace MicMeter.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4D4D;

    public event EventHandler? Pressed;
    public bool IsRegistered => Volatile.Read(ref _registeredFlag) != 0;

    private int _registeredFlag;
    private Thread? _thread;
#if WINDOWS
    private uint _threadId;
#endif
    private volatile bool _running;
    private uint _modifiers;
    private int _virtualKey;

    public bool Register(uint modifiers, int virtualKey)
    {
        Unregister();
        if (virtualKey == 0)
        {
            return false;
        }

        _modifiers = modifiers;
        _virtualKey = virtualKey;
        _running = true;
        _thread = new Thread(PlatformLoop) { IsBackground = true };
        _thread.Start();
        return true;
    }

    public void Unregister()
    {
        var wasRunning = _running;
        _running = false;
#if WINDOWS
        if (wasRunning && _threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
#else
        if (wasRunning)
        {
            QuitApplicationEventLoop();
        }
#endif
        _thread?.Join(2000);
        _thread = null;
#if WINDOWS
        _threadId = 0;
#endif
        Volatile.Write(ref _registeredFlag, 0);
    }

    public void Dispose() => Unregister();

    private void RaisePressed() => Pressed?.Invoke(this, EventArgs.Empty);

#if WINDOWS
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint ModNoRepeat = 0x4000;
    private static readonly nint HwndMessage = new(-3);
    private static readonly WndProcDelegate WndProc = WndProcHandler;
    private static GlobalHotkeyService? _current;
    private static bool _classRegistered;
    private static nint _messageWindow;

    private void PlatformLoop()
    {
        _current = this;
        _threadId = GetCurrentThreadId();
        _messageWindow = CreateMessageWindow();
        if (_messageWindow == IntPtr.Zero)
        {
            _running = false;
            return;
        }

        if (!RegisterHotKey(_messageWindow, HotkeyId, _modifiers | ModNoRepeat, (uint)_virtualKey))
        {
            DestroyWindow(_messageWindow);
            _messageWindow = IntPtr.Zero;
            _running = false;
            return;
        }

        Volatile.Write(ref _registeredFlag, 1);
        while (_running)
        {
            if (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }

        UnregisterHotKey(_messageWindow, HotkeyId);
        DestroyWindow(_messageWindow);
        _messageWindow = IntPtr.Zero;
        Volatile.Write(ref _registeredFlag, 0);
        _current = null;
    }

    private static nint CreateMessageWindow()
    {
        if (!_classRegistered)
        {
            var windowClass = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = WndProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = "MicMeterHotkeyWindow"
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                return IntPtr.Zero;
            }

            _classRegistered = true;
        }

        return CreateWindowEx(0, "MicMeterHotkeyWindow", string.Empty, 0, 0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
    }

    private static nint WndProcHandler(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _current?.RaisePressed();
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Hwnd;
        public uint Msg;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, nint windowHandle, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint windowHandle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
#else
    private const uint CarbonEventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint CarbonEventHotKeyPressed = 5;
    private const uint CarbonEventParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint CarbonTypeEventHotKeyId = 0x686B6964; // 'hkid'
    private const string CarbonDylib = "/System/Library/Frameworks/Carbon.framework/Carbon";

    private static readonly CarbonEventHandlerProc Handler = HandleHotKeyEvent;
    private static GlobalHotkeyService? _current;
    private IntPtr _hotKeyRef;
    private IntPtr _handlerRef;

    private void PlatformLoop()
    {
        _current = this;
        try
        {
            var target = GetEventDispatcherTarget();
            var spec = new[]
            {
                new EventTypeSpec { EventClass = CarbonEventClassKeyboard, EventKind = CarbonEventHotKeyPressed }
            };
            if (InstallEventHandler(target, Handler, (uint)spec.Length, spec, IntPtr.Zero, out _handlerRef) != 0)
            {
                _running = false;
                return;
            }

            var hotKeyId = new EventHotKeyId { Signature = 0x4D4D4D4D, Id = HotkeyId };
            if (RegisterEventHotKey(HotkeyPlatform.ToCarbonKeyCode(_virtualKey), HotkeyPlatform.ToCarbonModifiers(_modifiers), hotKeyId, target, 0, out _hotKeyRef) != 0)
            {
                RemoveEventHandler(_handlerRef);
                _handlerRef = IntPtr.Zero;
                _running = false;
                return;
            }

            Volatile.Write(ref _registeredFlag, 1);
            RunApplicationEventLoop();
        }
        finally
        {
            Volatile.Write(ref _registeredFlag, 0);
            if (_hotKeyRef != IntPtr.Zero)
            {
                UnregisterEventHotKey(_hotKeyRef);
                _hotKeyRef = IntPtr.Zero;
            }

            if (_handlerRef != IntPtr.Zero)
            {
                RemoveEventHandler(_handlerRef);
                _handlerRef = IntPtr.Zero;
            }

            _current = null;
        }
    }

    private static int HandleHotKeyEvent(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        var hotKeyId = new EventHotKeyId();
        uint actualType = 0;
        uint actualSize = 0;
        if (GetEventParameter(theEvent, CarbonEventParamDirectObject, CarbonTypeEventHotKeyId,
                out actualType, (uint)Marshal.SizeOf<EventHotKeyId>(), ref actualSize, ref hotKeyId) == 0 &&
            hotKeyId.Id == HotkeyId)
        {
            _current?.RaisePressed();
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public int Signature;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CarbonEventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport(CarbonDylib)]
    private static extern IntPtr GetEventDispatcherTarget();

    [DllImport(CarbonDylib)]
    private static extern int InstallEventHandler(IntPtr target, CarbonEventHandlerProc handler,
        uint numTypes, EventTypeSpec[] types, IntPtr userData, out IntPtr handlerRef);

    [DllImport(CarbonDylib)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(CarbonDylib)]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId hotKeyId,
        IntPtr target, uint options, out IntPtr refCon);

    [DllImport(CarbonDylib)]
    private static extern int UnregisterEventHotKey(IntPtr refCon);

    [DllImport(CarbonDylib)]
    private static extern int RunApplicationEventLoop();

    [DllImport(CarbonDylib)]
    private static extern int QuitApplicationEventLoop();

    [DllImport(CarbonDylib)]
    private static extern int GetEventParameter(IntPtr theEvent, uint name, uint desiredType,
        out uint actualType, uint bufferSize, ref uint actualSize, ref EventHotKeyId outData);
#endif
}
