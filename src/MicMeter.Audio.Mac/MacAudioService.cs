using MicMeter.Models;

namespace MicMeter.Audio;

public sealed class MacAudioService : IAudioService
{
    // UID of the MicMeter virtual loopback device (drivers/MicMeterLoopback).
    // Set from the driver's kDevice_UID when kDriver_Name is "MicMeter".
    private const string MicMeterLoopbackUid = "MicMeter_UID";

    private readonly List<MacAudioMonitor> _monitors = [];
    private LoopbackMixer _loopbackMixer = new();
    private PluginLevelMonitor? _pluginMonitor;
    private int _bufferFrameSize;
    private string? _listeningOutputDeviceId;
    private AudioRoutingMode _routingMode = AudioRoutingMode.Loopback;

    public IReadOnlyList<IAudioMonitor> Monitors =>
        _routingMode == AudioRoutingMode.Plugin && _pluginMonitor is not null
            ? [_pluginMonitor]
            : _monitors;

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices() =>
        CoreAudio.GetInputDevices()
            .Select(device => new AudioDeviceInfo(device.Uid, device.Name))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        CoreAudio.GetOutputDevices()
            .Select(device => new AudioDeviceInfo(device.Uid, device.Name))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public void SetListeningOutputDevice(string? deviceId)
    {
        _listeningOutputDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        foreach (var monitor in _monitors)
        {
            monitor.SetListeningOutputDevice(_listeningOutputDeviceId);
        }

        _pluginMonitor?.SetListeningOutputDevice(_listeningOutputDeviceId);
    }

    public void SetBufferFrameSize(int bufferFrameSize)
    {
        _bufferFrameSize = bufferFrameSize < 0 ? 0 : bufferFrameSize;
    }

    public void SetRoutingMode(AudioRoutingMode mode)
    {
        _routingMode = mode;
    }

    public IReadOnlyList<string> Connect(IReadOnlyCollection<string> preferredDeviceIds)
    {
        try { System.IO.File.AppendAllText("/tmp/micmeter_debug.log", DateTime.Now.ToString("HH:mm:ss.fff ") + $"Connect: routingMode={_routingMode}" + Environment.NewLine); }
        catch { }

        MacPluginInstaller.EnsureMutePluginInstalled();

        DisposeMonitors();
        _loopbackMixer.Stop();
        _loopbackMixer.Dispose();
        _loopbackMixer = new LoopbackMixer(_bufferFrameSize);
        _pluginMonitor?.Dispose();
        _pluginMonitor = null;

        if (_routingMode == AudioRoutingMode.Plugin)
        {
            return ConnectPluginMode(preferredDeviceIds);
        }

        var deviceIds = preferredDeviceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

        if (deviceIds.Count == 0)
        {
            var defaultId = CoreAudio.GetDefaultInputDeviceId();
            var defaultUid = CoreAudio.GetInputDevices()
                .FirstOrDefault(device => device.ObjectId == defaultId)?.Uid;
            if (string.IsNullOrWhiteSpace(defaultUid))
            {
                return [];
            }

            deviceIds.Add(defaultUid);
        }

        foreach (var deviceId in deviceIds)
        {
            try
            {
                var monitor = new MacAudioMonitor(deviceId, _bufferFrameSize);
                monitor.SetListeningOutputDevice(_listeningOutputDeviceId);
                _monitors.Add(monitor);
            }
            catch
            {
                // One unavailable device must not prevent the other meters from starting.
            }
        }

        StartLoopbackMixer();

        return _monitors.Select(monitor => monitor.DeviceId).ToArray();
    }

    private IReadOnlyList<string> ConnectPluginMode(IReadOnlyCollection<string> preferredDeviceIds)
    {
        try { System.IO.File.AppendAllText("/tmp/micmeter_debug.log", DateTime.Now.ToString("HH:mm:ss.fff ") + "ConnectPluginMode: called" + Environment.NewLine); }
        catch { }

        var shared = MicMeterSharedMemory.Open();
        _pluginMonitor = new PluginLevelMonitor(shared);

        try { System.IO.File.AppendAllText("/tmp/micmeter_debug.log", DateTime.Now.ToString("HH:mm:ss.fff ") + $"ConnectPluginMode: shared={shared is not null}, IsRunning={_pluginMonitor.IsRunning}" + Environment.NewLine); }
        catch { }

        return [_pluginMonitor.DeviceId];
    }

    /// <summary>
    /// Selects which input devices are mixed into the MicMeter virtual loopback
    /// device. An empty list means "route only the first device" (legacy
    /// behaviour). The loopback device itself is never a source (no feedback).
    /// No-op in Plugin mode.
    /// </summary>
    public void SetLoopbackDevices(IReadOnlyCollection<string> deviceIds)
    {
        if (_routingMode == AudioRoutingMode.Plugin)
        {
            return;
        }

        var requested = deviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id) &&
                         !id.Equals(MicMeterLoopbackUid, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Empty means fall back to the topmost monitor.
        if (requested.Count == 0 && _monitors.Count > 0)
        {
            var top = _monitors[0];
            if (!top.DeviceId.Equals(MicMeterLoopbackUid, StringComparison.Ordinal))
            {
                requested.Add(top.DeviceId);
            }
        }

        var sources = _monitors
            .Where(monitor => requested.Contains(monitor.DeviceId))
            .ToList();

        _loopbackMixer.SetSources(sources);
    }

    /// <summary>
    /// Starts the loopback mixer bound to the MicMeter virtual device, if the
    /// driver is installed. Best-effort: never fails device connection over it.
    /// </summary>
    private void StartLoopbackMixer()
    {
        if (_routingMode == AudioRoutingMode.Plugin)
        {
            return;
        }

        try
        {
            var loopbackDeviceId = CoreAudio.ResolveDeviceId(MicMeterLoopbackUid);
            if (loopbackDeviceId == 0)
            {
                // Driver not installed; meters and listening still work.
                return;
            }

            _loopbackMixer.Start(loopbackDeviceId);
        }
        catch
        {
            // Loopback is best-effort.
        }
    }

    public void ToggleMute(string deviceId)
    {
        if (_routingMode == AudioRoutingMode.Plugin)
        {
            _pluginMonitor?.ToggleMute();
            return;
        }

        FineTuneClient.ToggleInputMute(deviceId);
        _monitors.FirstOrDefault(monitor => monitor.DeviceId == deviceId)?.ToggleMute();
    }

    public bool ToggleListening(string deviceId)
    {
        if (_routingMode == AudioRoutingMode.Plugin)
        {
            if (_pluginMonitor is null)
            {
                LogDebug($"ToggleListening: mode=Plugin device={deviceId} FAILED no plugin monitor");
                return false;
            }

            var requested = !_pluginMonitor.IsListening;
            var result = _pluginMonitor.SetListening(requested);
            LogDebug($"ToggleListening: mode=Plugin device={deviceId} requested={requested} result={result} isListening={_pluginMonitor.IsListening}");
            return result;
        }

        var monitor = _monitors.FirstOrDefault(monitor => monitor.DeviceId == deviceId);
        return monitor is not null && monitor.SetListening(!monitor.IsListening);
    }

    public void ToggleMuteAll()
    {
        if (_routingMode == AudioRoutingMode.Plugin)
        {
            _pluginMonitor?.ToggleMute();
            return;
        }

        var shouldMute = _monitors.Any(monitor => !monitor.IsMuted);
        SetMuteAll(shouldMute);
    }

    public void SetMuteAll(bool muted)
    {
        if (_routingMode == AudioRoutingMode.Plugin)
        {
            _pluginMonitor?.SetMute(muted);
            return;
        }

        FineTuneClient.SetInputMuteAll(muted);
        foreach (var monitor in _monitors)
        {
            monitor.SetMute(muted);
        }
    }

    public void Dispose()
    {
        _loopbackMixer.Dispose();
        _pluginMonitor?.Dispose();
        _pluginMonitor = null;
        DisposeMonitors();
    }

    private static void LogDebug(string message)
    {
        try { System.IO.File.AppendAllText("/tmp/micmeter_debug.log", DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine); }
        catch { }
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
