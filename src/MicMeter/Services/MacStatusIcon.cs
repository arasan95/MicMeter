#if !WINDOWS
using System.Runtime.InteropServices;
using System.Text;

namespace MicMeter.Services;

public sealed class MacStatusIcon : IStatusIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    private static readonly nint NsStatusBarClass = objc_getClass("NSStatusBar");
    private static readonly nint NsButtonClass = objc_getClass("NSButton");
    private static readonly nint NsImageClass = objc_getClass("NSImage");
    private static readonly nint NsDataClass = objc_getClass("NSData");
    private static readonly nint NsMenuClass = objc_getClass("NSMenu");
    private static readonly nint NsMenuItemClass = objc_getClass("NSMenuItem");
    private static readonly nint NsStringClass = objc_getClass("NSString");
    private static readonly nint NsObjectClass = objc_getClass("NSObject");

    private static readonly nint SelAlloc = sel_registerName("alloc");
    private static readonly nint SelInit = sel_registerName("init");
    private static readonly nint SelSystemStatusBar = sel_registerName("systemStatusBar");
    private static readonly nint SelStatusItemWithLength = sel_registerName("statusItemWithLength:");
    private static readonly nint SelSetView = sel_registerName("setView:");
    private static readonly nint SelSetImage = sel_registerName("setImage:");
    private static readonly nint SelSetBordered = sel_registerName("setBordered:");
    private static readonly nint SelSetSize = sel_registerName("setSize:");
    private static readonly nint SelSetToolTip = sel_registerName("setToolTip:");
    private static readonly nint SelSetTarget = sel_registerName("setTarget:");
    private static readonly nint SelSetAction = sel_registerName("setAction:");
    private static readonly nint SelAddItem = sel_registerName("addItem:");
    private static readonly nint SelSeparatorItem = sel_registerName("separatorItem");
    private static readonly nint SelInitWithData = sel_registerName("initWithData:");
    private static readonly nint SelInitWithTitle = sel_registerName("initWithTitle:action:keyEquivalent:");
    private static readonly nint SelDataWithBytes = sel_registerName("dataWithBytes:length:");
    private static readonly nint SelStringWithUtf8 = sel_registerName("stringWithUTF8String:");
    private static readonly nint SelPopUpMenu = sel_registerName("popUpContextMenu:withEvent:forView:");
    private static readonly nint SelRemoveStatusItem = sel_registerName("removeStatusItem:");

    private static readonly nint SelMouseDown = sel_registerName("mouseDown:");
    private static readonly nint SelRightMouseDown = sel_registerName("rightMouseDown:");
    private static readonly nint SelShowHide = sel_registerName("showHide:");
    private static readonly nint SelToggleMute = sel_registerName("toggleMute:");
    private static readonly nint SelSettings = sel_registerName("settings:");
    private static readonly nint SelQuit = sel_registerName("quit:");

    private static readonly MouseHandler MouseDownImpl = HandleMouseDown;
    private static readonly MouseHandler RightMouseDownImpl = HandleRightMouseDown;
    private static readonly ActionHandler ShowHideImpl = HandleShowHide;
    private static readonly ActionHandler ToggleMuteImpl = HandleToggleMute;
    private static readonly ActionHandler SettingsImpl = HandleSettings;
    private static readonly ActionHandler QuitImpl = HandleQuit;

    private static nint _buttonClass;
    private static nint _handlerClass;
    private static MacStatusIcon? _active;

    private readonly StatusIconActions _actions;
    private readonly Func<string, string, string> _translate;
    private nint _statusItem;
    private nint _button;
    private nint _menu;
    private nint _menuTarget;

    public MacStatusIcon(byte[] iconPng, StatusIconActions actions, Func<string, string, string> translate)
    {
        _actions = actions;
        _translate = translate;
        EnsureClassesRegistered();
        _active = this;

        var statusBar = objc_msgSend(NsStatusBarClass, SelSystemStatusBar);
        _statusItem = objc_msgSend_double(statusBar, SelStatusItemWithLength, -1.0);

        _button = objc_msgSend(_buttonClass, SelAlloc);
        _button = objc_msgSend(_button, SelInit);
        // A default NSButton draws its own gray background/border; disable both
        // so the transparent PNG blends into the menu bar like a normal status item.
        objc_msgSend_bool(_button, SelSetBordered, false);
        objc_msgSend_ptr(_button, SelSetImage, CreateImage(iconPng));
        objc_msgSend_ptr(_statusItem, SelSetView, _button);

        BuildMenu();
    }

    public event EventHandler? LeftClicked;

    public void SetIcon(byte[] pngBytes) =>
        objc_msgSend_ptr(_button, SelSetImage, CreateImage(pngBytes));

    public void SetToolTip(string text) =>
        objc_msgSend_ptr(_button, SelSetToolTip, CreateNSString(text));

    public void UpdateLanguage() => BuildMenu();

    public void Dispose()
    {
        if (_active == this)
        {
            _active = null;
        }

        if (_statusItem != 0)
        {
            var statusBar = objc_msgSend(NsStatusBarClass, SelSystemStatusBar);
            objc_msgSend_ptr(statusBar, SelRemoveStatusItem, _statusItem);
            _statusItem = 0;
        }

        _button = 0;
        _menu = 0;
        _menuTarget = 0;
    }

    private void BuildMenu()
    {
        var menu = objc_msgSend(NsMenuClass, SelAlloc);
        menu = objc_msgSend(menu, SelInit);
        _menu = menu;

        var target = objc_msgSend(_handlerClass, SelAlloc);
        target = objc_msgSend(target, SelInit);
        _menuTarget = target;

        AddMenuItem(menu, _translate("表示 / 非表示", "Show / Hide"), SelShowHide, target);
        AddMenuItem(menu, _translate("すべてミュート切り替え", "Toggle mute all"), SelToggleMute, target);
        AddMenuItem(menu, _translate("設定", "Settings"), SelSettings, target);
        objc_msgSend_ptr(menu, SelAddItem, objc_msgSend(NsMenuItemClass, SelSeparatorItem));
        AddMenuItem(menu, _translate("終了", "Quit"), SelQuit, target);
    }

    private void AddMenuItem(nint menu, string title, nint action, nint target)
    {
        var item = objc_msgSend(NsMenuItemClass, SelAlloc);
        item = objc_msgSend_3(item, SelInitWithTitle, CreateNSString(title), action, CreateNSString(string.Empty));
        objc_msgSend_ptr(item, SelSetTarget, target);
        objc_msgSend_ptr(menu, SelAddItem, item);
    }

    private static void HandleMouseDown(nint self, nint selector, nint theEvent)
    {
        _active?.LeftClicked?.Invoke(_active, EventArgs.Empty);
    }

    private static void HandleRightMouseDown(nint self, nint selector, nint theEvent)
    {
        if (_active is { } active && active._menu != 0)
        {
            objc_msgSend_3(NsMenuClass, SelPopUpMenu, active._menu, theEvent, active._button);
        }
    }

    private static void HandleShowHide(nint self, nint selector, nint sender) =>
        _active?._actions.ShowHide();

    private static void HandleToggleMute(nint self, nint selector, nint sender) =>
        _active?._actions.ToggleMute();

    private static void HandleSettings(nint self, nint selector, nint sender) =>
        _active?._actions.Settings();

    private static void HandleQuit(nint self, nint selector, nint sender) =>
        _active?._actions.Quit();

    private static void EnsureClassesRegistered()
    {
        if (_buttonClass != 0)
        {
            return;
        }

        _buttonClass = objc_allocateClassPair(NsButtonClass, "MicMeterStatusButton", UIntPtr.Zero);
        class_addMethod(_buttonClass, SelMouseDown, Marshal.GetFunctionPointerForDelegate(MouseDownImpl), "v@:@");
        class_addMethod(_buttonClass, SelRightMouseDown, Marshal.GetFunctionPointerForDelegate(RightMouseDownImpl), "v@:@");
        objc_registerClassPair(_buttonClass);

        _handlerClass = objc_allocateClassPair(NsObjectClass, "MicMeterMenuHandler", UIntPtr.Zero);
        class_addMethod(_handlerClass, SelShowHide, Marshal.GetFunctionPointerForDelegate(ShowHideImpl), "v@:@");
        class_addMethod(_handlerClass, SelToggleMute, Marshal.GetFunctionPointerForDelegate(ToggleMuteImpl), "v@:@");
        class_addMethod(_handlerClass, SelSettings, Marshal.GetFunctionPointerForDelegate(SettingsImpl), "v@:@");
        class_addMethod(_handlerClass, SelQuit, Marshal.GetFunctionPointerForDelegate(QuitImpl), "v@:@");
        objc_registerClassPair(_handlerClass);
    }

    private static nint CreateNSString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + '\0');
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        try
        {
            return objc_msgSend_ptr(NsStringClass, SelStringWithUtf8, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static nint CreateImage(byte[] png)
    {
        var handle = GCHandle.Alloc(png, GCHandleType.Pinned);
        try
        {
            var data = objc_msgSend_uiptr(NsDataClass, SelDataWithBytes, handle.AddrOfPinnedObject(), new UIntPtr((uint)png.Length));
            var image = objc_msgSend(NsImageClass, SelAlloc);
            image = objc_msgSend_ptr(image, SelInitWithData, data);
            objc_msgSend_size(image, SelSetSize, new NSSize { Width = 18, Height = 18 });
            return image;
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MouseHandler(nint self, nint selector, nint theEvent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActionHandler(nint self, nint selector, nint sender);

    [DllImport(ObjC)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjC)]
    private static extern nint objc_allocateClassPair(nint superclass, string name, UIntPtr extraBytes);

    [DllImport(ObjC)]
    private static extern void objc_registerClassPair(nint cls);

    [DllImport(ObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(nint cls, nint name, nint imp, string types);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_ptr(nint receiver, nint selector, nint arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool flag);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_double(nint receiver, nint selector, double arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_uiptr(nint receiver, nint selector, nint arg1, UIntPtr arg2);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_3(nint receiver, nint selector, nint arg1, nint arg2, nint arg3);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_size(nint receiver, nint selector, NSSize size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSSize
    {
        public double Width;
        public double Height;
    }
}
#endif
