using MicMeter.Models;
using NAudio.CoreAudioApi;

namespace MicMeter.Audio;

public sealed class WindowsAudioService : IAudioService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly List<WindowsAudioMonitor> _monitors = [];
    private string? _listeningOutputDeviceId;

    public IReadOnlyList<IAudioMonitor> Monitors => _monitors;

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        try
        {
            return _enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        try
        {
            return _enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public void SetListeningOutputDevice(string? deviceId)
    {
        _listeningOutputDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        foreach (var monitor in _monitors)
        {
            monitor.SetListeningOutputDevice(_listeningOutputDeviceId);
        }
    }

    public IReadOnlyList<string> Connect(IReadOnlyCollection<string> preferredDeviceIds)
    {
        DisposeMonitors();
        var deviceIds = preferredDeviceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

        if (deviceIds.Count == 0)
        {
            try
            {
                using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                deviceIds.Add(defaultDevice.ID);
            }
            catch
            {
                return [];
            }
        }

        foreach (var deviceId in deviceIds)
        {
            try
            {
                var device = _enumerator.GetDevice(deviceId);
                if (device.State != DeviceState.Active)
                {
                    device.Dispose();
                    continue;
                }

                var monitor = new WindowsAudioMonitor(device);
                monitor.SetListeningOutputDevice(_listeningOutputDeviceId);
                _monitors.Add(monitor);
            }
            catch
            {
                // One unavailable device must not prevent the other meters from starting.
            }
        }

        return _monitors.Select(monitor => monitor.DeviceId).ToArray();
    }

    public void ToggleMute(string deviceId)
    {
        _monitors.FirstOrDefault(monitor => monitor.DeviceId == deviceId)?.ToggleMute();
    }

    public bool ToggleListening(string deviceId)
    {
        var monitor = _monitors.FirstOrDefault(monitor => monitor.DeviceId == deviceId);
        return monitor is not null && monitor.SetListening(!monitor.IsListening);
    }

    public void ToggleMuteAll()
    {
        var shouldMute = _monitors.Any(monitor => !monitor.IsMuted);
        SetMuteAll(shouldMute);
    }

    public void SetMuteAll(bool muted)
    {
        foreach (var monitor in _monitors)
        {
            monitor.SetMute(muted);
        }
    }

    // Loopback routing is macOS-only (the MicMeter virtual audio device does not
    // exist on Windows), so this is intentionally a no-op.
    public void SetLoopbackDevices(IReadOnlyCollection<string> deviceIds)
    {
    }

    // macOS-only buffer-size control; Windows uses the WASAPI default.
    public void SetBufferFrameSize(int bufferFrameSize)
    {
    }

    // macOS-only routing mode; Windows always uses WASAPI capture.
    public void SetRoutingMode(AudioRoutingMode mode)
    {
    }

    public void Dispose()
    {
        DisposeMonitors();
        _enumerator.Dispose();
    }

    private void DisposeMonitors()
    {
        foreach (var monitor in _monitors)
        {
            monitor.Dispose();
        }

        _monitors.Clear();
    }
}
