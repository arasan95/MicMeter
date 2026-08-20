using MicMeter.Services;

namespace MicMeter.Core.Tests;

public sealed class HotkeyFormatTests
{
    [Fact]
    public void Format_FormatsModifiersAndKey()
    {
        Assert.Equal("Ctrl + Alt + M", HotkeyFormat.Format(0x0001 | 0x0002, 0x4D));
    }

    [Fact]
    public void Format_ReturnsUnsetForEmptyKey()
    {
        Assert.Equal("未設定", HotkeyFormat.Format(0, 0));
    }

    [Fact]
    public void KeyName_FormatsFunctionKeys()
    {
        Assert.Equal("F5", HotkeyFormat.KeyName(0x74));
    }
}
