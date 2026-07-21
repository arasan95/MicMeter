using MicMeter.Services;

namespace MicMeter.Tests;

public sealed class GlobalHotkeyServiceTests
{
    [Fact]
    public void Format_FormatsModifiersAndKey()
    {
        Assert.Equal("Ctrl + Alt + M", GlobalHotkeyService.Format(0x0001 | 0x0002, 0x4D));
    }

    [Fact]
    public void Format_ReturnsUnsetForEmptyKey()
    {
        Assert.Equal("未設定", GlobalHotkeyService.Format(0, 0));
    }
}
