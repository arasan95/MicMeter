using System.Diagnostics;

namespace MicMeter.Audio;

public static class FineTuneClient
{
    private const string Scheme = "finetune";
    private static int _savedSystemInputVolume = 100;

    public static bool IsFineTuneRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("FineTune");
            if (processes.Length > 0)
            {
                return true;
            }
        }
        catch
        {
            // Ignore process query errors
        }

        return false;
    }

    public static void ToggleInputMute(string? deviceUid = null)
    {
        var url = string.IsNullOrWhiteSpace(deviceUid)
            ? $"{Scheme}://toggle-input-mute"
            : $"{Scheme}://toggle-input-mute?device={Uri.EscapeDataString(deviceUid)}";
        OpenUrl(url);
    }

    public static void ToggleInputMuteAll()
    {
        OpenUrl($"{Scheme}://toggle-input-mute?all=true");
    }

    public static void SetInputMute(bool muted, string? deviceUid = null)
    {
        var url = string.IsNullOrWhiteSpace(deviceUid)
            ? $"{Scheme}://set-input-mute?muted={muted.ToString().ToLowerInvariant()}"
            : $"{Scheme}://set-input-mute?device={Uri.EscapeDataString(deviceUid)}&muted={muted.ToString().ToLowerInvariant()}";
        OpenUrl(url);
        ApplySystemInputMute(muted);
    }

    public static void SetInputMuteAll(bool muted)
    {
        OpenUrl($"{Scheme}://set-input-mute?all=true&muted={muted.ToString().ToLowerInvariant()}");
        ApplySystemInputMute(muted);
    }

    private static void ApplySystemInputMute(bool muted)
    {
        try
        {
            if (muted)
            {
                var currentVol = GetSystemInputVolume();
                if (currentVol > 0)
                {
                    _savedSystemInputVolume = currentVol;
                }
                RunAppleScriptDetached("set volume input volume 0");
            }
            else
            {
                var restoreVol = _savedSystemInputVolume > 0 ? _savedSystemInputVolume : 100;
                RunAppleScriptDetached($"set volume input volume {restoreVol}");
            }
        }
        catch
        {
            // Suppress fallback errors
        }
    }

    private static int GetSystemInputVolume()
    {
        try
        {
            var output = RunAppleScript("input volume of (get volume settings)");
            if (int.TryParse(output.Trim(), out var vol))
            {
                return vol;
            }
        }
        catch
        {
            // Suppress error
        }

        return _savedSystemInputVolume;
    }

    private static string RunAppleScript(string script)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = $"-e \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);
                return output;
            }
        }
        catch
        {
            // Suppress error
        }

        return string.Empty;
    }

    // Runs an AppleScript without waiting for it to finish, so the UI thread is
    // not blocked by osascript startup/shutdown latency during mute toggling.
    private static void RunAppleScriptDetached(string script)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = $"-e \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            _ = Process.Start(startInfo);
        }
        catch
        {
            // Suppress error
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                Arguments = $"-g \"{url}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            _ = Process.Start(startInfo);
        }
        catch
        {
            // Suppress error if open fails
        }
    }
}
