using System.IO;

namespace MicMeter.Audio;

/// <summary>
/// Keeps the MicMeter Mute LV2 plugin available to LV2 hosts (Carla, Element,
/// ...) by installing it into the per-user LV2 directory on startup. The plugin
/// bundle ships inside the app at Contents/Resources/LV2, so installing the app
/// is enough; no admin rights are needed for the user LV2 directory.
/// </summary>
internal static class MacPluginInstaller
{
    /// <summary>
    /// Copies the embedded MicMeter Mute LV2 plugin into
    /// ~/Library/Audio/Plug-Ins/LV2 when it is missing or differs from the
    /// embedded bundle. Returns true when the destination now matches.
    /// </summary>
    internal static bool EnsureMutePluginInstalled()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            // Published app layout: <app>/Contents/MacOS -> <app>/Contents/Resources/LV2
            var embedded = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "LV2", "MicMeterMute.lv2"));
            if (!Directory.Exists(embedded))
            {
                return false;
            }

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var destDir = Path.Combine(profile, "Library", "Audio", "Plug-Ins", "LV2");
            var dest = Path.Combine(destDir, "MicMeterMute.lv2");

            if (Directory.Exists(dest) && SameBundle(embedded, dest))
            {
                return true;
            }

            Directory.CreateDirectory(destDir);
            if (Directory.Exists(dest))
            {
                Directory.Delete(dest, recursive: true);
            }

            CopyDirectory(embedded, dest);

            LogDebug($"MacPluginInstaller: installed plugin to {dest}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SameBundle(string a, string b)
    {
        var soA = Path.Combine(a, "micmeter_mute.so");
        var soB = Path.Combine(b, "micmeter_mute.so");
        return File.Exists(soA) &&
               File.Exists(soB) &&
               HashOf(soA) == HashOf(soB);
    }

    private static string HashOf(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDir))
        {
            var name = Path.GetFileName(entry);
            var target = Path.Combine(destDir, name);
            if (Directory.Exists(entry))
            {
                CopyDirectory(entry, target);
            }
            else
            {
                File.Copy(entry, target, overwrite: true);
            }
        }
    }

    private static void LogDebug(string message)
    {
        try
        {
            File.AppendAllText(
                "/tmp/micmeter_debug.log",
                DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
        }
        catch
        {
        }
    }
}