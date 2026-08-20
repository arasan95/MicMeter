using MicMeter.Services;

namespace MicMeter.Core.Tests;

public sealed class LocalizationStringsTests
{
    [Fact]
    public void Translate_ReturnsEnglishLabel()
    {
        Assert.Equal("MicMeter Settings", LocalizationStrings.Translate("MicMeter 設定", true));
        Assert.Equal("Menu bar meter", LocalizationStrings.Translate("メニューバーメーター", true));
    }

    [Fact]
    public void Translate_ReturnsJapaneseLabel()
    {
        Assert.Equal("MicMeter 設定", LocalizationStrings.Translate("MicMeter Settings", false));
        Assert.Equal("メニューバーメーター", LocalizationStrings.Translate("Menu bar meter", false));
    }

    [Fact]
    public void Translate_KeepsUnknownText()
    {
        Assert.Equal("Unknown", LocalizationStrings.Translate("Unknown", true));
    }
}
