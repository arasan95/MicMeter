using System.IO;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace MicMeter.Services;

public static class StartupService
{
    private const string LaunchAgentFileName = "com.arasan95.MicMeter.plist";

    public static bool IsEnabled()
    {
#if WINDOWS
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
#else
        return File.Exists(LaunchAgentPath);
#endif
    }

    public static void SetEnabled(bool enabled)
    {
#if WINDOWS
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath}\" --startup", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
#else
        if (enabled)
        {
            WriteLaunchAgent();
        }
        else
        {
            TryDeleteLaunchAgent();
        }
#endif
    }

    private static string ExecutablePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Unable to determine the MicMeter executable path.");

#if WINDOWS
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MicMeter";
#else
    private static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library",
        "LaunchAgents",
        LaunchAgentFileName);

    private static void WriteLaunchAgent()
    {
        var directory = Path.GetDirectoryName(LaunchAgentPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(LaunchAgentPath, LaunchAgentGenerator.GeneratePlist(ExecutablePath));
    }

    private static void TryDeleteLaunchAgent()
    {
        try
        {
            if (File.Exists(LaunchAgentPath))
            {
                File.Delete(LaunchAgentPath);
            }
        }
        catch (IOException)
        {
            // Leaving a stale plist is preferable to failing the settings save.
        }
    }
#endif
}
