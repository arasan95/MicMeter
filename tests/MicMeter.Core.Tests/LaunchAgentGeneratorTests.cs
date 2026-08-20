using MicMeter.Services;

namespace MicMeter.Core.Tests;

public sealed class LaunchAgentGeneratorTests
{
    [Fact]
    public void GeneratePlist_ContainsLabelAndStartupArgument()
    {
        var plist = LaunchAgentGenerator.GeneratePlist("/Applications/MicMeter.app/Contents/MacOS/MicMeter");

        Assert.Contains(LaunchAgentGenerator.Label, plist);
        Assert.Contains("--startup", plist);
        Assert.Contains("RunAtLoad", plist);
    }

    [Fact]
    public void GeneratePlist_EscapesXmlSpecialCharacters()
    {
        var plist = LaunchAgentGenerator.GeneratePlist("/tmp/a&b<MicMeter>");

        Assert.DoesNotContain("a&b<MicMeter>", plist);
        Assert.Contains("&amp;", plist);
    }
}
