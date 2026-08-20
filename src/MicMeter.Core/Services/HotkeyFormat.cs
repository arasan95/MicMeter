namespace MicMeter.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Command = 0x0008 // Maps to the Windows "Win" key on Windows and Command on macOS.
}

public static class HotkeyFormat
{
    public static string Format(uint modifiers, int virtualKey)
    {
        if (virtualKey == 0)
        {
            return "未設定";
        }

        var parts = new List<string>();
        if ((modifiers & (uint)HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & (uint)HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & (uint)HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & (uint)HotkeyModifiers.Command) != 0)
        {
            parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        }

        parts.Add(KeyName(virtualKey));
        return string.Join(" + ", parts);
    }

    public static string KeyName(int virtualKey) => virtualKey switch
    {
        0x0D => "Enter",
        0x1B => "Esc",
        0x20 => "Space",
        0x08 => "Backspace",
        0x09 => "Tab",
        >= 0x70 and <= 0x7B => $"F{virtualKey - 0x70 + 1}",
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        _ => $"Key{virtualKey}"
    };
}
