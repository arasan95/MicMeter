using System.IO;

namespace MicMeter.Services;

public static class AppPaths
{
    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    public static string SettingsDirectory
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "MicMeter");
            }

            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MicMeter");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MicMeter");
        }
    }
}
