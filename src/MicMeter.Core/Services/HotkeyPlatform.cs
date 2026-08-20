namespace MicMeter.Services;

public static class HotkeyPlatform
{
    private const uint CarbonControlKey = 1 << 12;
    private const uint CarbonOptionKey = 1 << 11;
    private const uint CarbonShiftKey = 1 << 9;
    private const uint CarbonCmdKey = 1 << 8;

    public static uint ToCarbonModifiers(uint neutralModifiers)
    {
        uint result = 0;
        if ((neutralModifiers & (uint)HotkeyModifiers.Alt) != 0) result |= CarbonOptionKey;
        if ((neutralModifiers & (uint)HotkeyModifiers.Control) != 0) result |= CarbonControlKey;
        if ((neutralModifiers & (uint)HotkeyModifiers.Shift) != 0) result |= CarbonShiftKey;
        if ((neutralModifiers & (uint)HotkeyModifiers.Command) != 0) result |= CarbonCmdKey;
        return result;
    }

    public static uint ToCarbonKeyCode(int virtualKey)
    {
        if (virtualKey >= 0x41 && virtualKey <= 0x5A)
        {
            return (char)virtualKey switch
            {
                'A' => 0, 'S' => 1, 'D' => 2, 'F' => 3, 'H' => 4, 'G' => 5, 'Z' => 6, 'X' => 7,
                'C' => 8, 'V' => 9, 'B' => 11, 'Q' => 12, 'W' => 13, 'E' => 14, 'R' => 15, 'Y' => 16,
                'T' => 17, 'O' => 31, 'U' => 32, 'I' => 34, 'P' => 35, 'L' => 37, 'J' => 38, 'K' => 40,
                'N' => 45, 'M' => 46,
                _ => 0
            };
        }

        if (virtualKey >= 0x30 && virtualKey <= 0x39)
        {
            return (char)virtualKey switch
            {
                '0' => 29, '1' => 18, '2' => 19, '3' => 20, '4' => 21, '5' => 23, '6' => 22,
                '7' => 26, '8' => 28, '9' => 25,
                _ => 0
            };
        }

        return virtualKey switch
        {
            0x0D => 36, // Return
            0x1B => 53, // Escape
            0x20 => 49, // Space
            0x08 => 51, // Delete/Backspace
            0x09 => 48, // Tab
            0x70 => 122, 0x71 => 120, 0x72 => 99, 0x73 => 118, 0x74 => 96, 0x75 => 97,
            0x76 => 98, 0x77 => 100, 0x78 => 101, 0x79 => 109, 0x7A => 103, 0x7B => 111,
            _ => 0
        };
    }
}
