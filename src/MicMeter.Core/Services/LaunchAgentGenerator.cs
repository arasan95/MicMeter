using System.Security;

namespace MicMeter.Services;

public static class LaunchAgentGenerator
{
    public const string Label = "com.arasan95.MicMeter";

    public static string GeneratePlist(string executablePath)
    {
        var executable = SecurityElement.Escape(executablePath);
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "<dict>\n" +
            "  <key>Label</key><string>" + Label + "</string>\n" +
            "  <key>ProgramArguments</key>\n" +
            "  <array><string>" + executable + "</string><string>--startup</string></array>\n" +
            "  <key>RunAtLoad</key><true/>\n" +
            "  <key>ProcessType</key><string>Interactive</string>\n" +
            "</dict>\n" +
            "</plist>\n";
    }
}
