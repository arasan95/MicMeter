using MicMeter.Models;
using NAudio.CoreAudioApi;

namespace MicMeter.Audio;

public sealed class MicrophoneService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly List<MicrophoneMonitor> _monitors = [];

    public IReadOnlyList<MicrophoneMonitor> Monitors => _monitors;

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

                _monitors.Add(new MicrophoneMonitor(device));
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
        foreach (var monitor in _monitors)
        {
            monitor.SetMute(shouldMute);
        }
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
