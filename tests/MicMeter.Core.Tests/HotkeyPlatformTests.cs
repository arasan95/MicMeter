using MicMeter.Services;

namespace MicMeter.Core.Tests;

public sealed class HotkeyPlatformTests
{
    [Theory]
    [InlineData('M', 46)]
    [InlineData('A', 0)]
    [InlineData('Z', 6)]
    [InlineData('0', 29)]
    [InlineData('9', 25)]
    public void ToCarbonKeyCode_ConvertsLettersAndDigits(char key, uint expected)
    {
        Assert.Equal(expected, HotkeyPlatform.ToCarbonKeyCode((int)key));
    }

    [Fact]
    public void ToCarbonKeyCode_ConvertsSpecialKeys()
    {
        Assert.Equal(36u, HotkeyPlatform.ToCarbonKeyCode(0x0D));
        Assert.Equal(53u, HotkeyPlatform.ToCarbonKeyCode(0x1B));
        Assert.Equal(49u, HotkeyPlatform.ToCarbonKeyCode(0x20));
        Assert.Equal(122u, HotkeyPlatform.ToCarbonKeyCode(0x70));
    }

    [Fact]
    public void ToCarbonModifiers_MapsNeutralFlags()
    {
        var modifiers = (uint)(HotkeyModifiers.Control | HotkeyModifiers.Alt);
        Assert.Equal((uint)(1 << 12 | 1 << 11), HotkeyPlatform.ToCarbonModifiers(modifiers));
    }
}
