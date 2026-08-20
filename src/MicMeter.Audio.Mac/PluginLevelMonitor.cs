namespace MicMeter.Audio;

/// <summary>
/// Level monitor backed by the MicMeter Mute LV2 plugin's shared-memory block.
/// Used in Plugin routing mode: the plugin publishes the input peak, and MicMeter
/// writes the mute flag back through the same block, so the existing overlay /
/// menu-bar meter and mute UI keep working unchanged. The "listen" control is
/// served by <see cref="PluginSharedAudioListener"/>, which plays back the audio
/// the plugin is already processing from the shared ring buffer — no microphone
/// capture is involved.
/// </summary>
internal sealed class PluginLevelMonitor : IAudioMonitor
{
    private readonly MicMeterSharedMemory? _shared;
    private PluginSharedAudioListener? _audioListener;
    private string? _outputDeviceUid;
    private bool _muted;
    private bool _listening;
    private bool _disposed;

    public PluginLevelMonitor(MicMeterSharedMemory? shared)
    {
        _shared = shared;
        if (shared is not null)
        {
            var pluginFrames = (int)shared.ReadRingFrames();
            if (pluginFrames != 0 && pluginFrames != MicMeterSharedMemory.RingFrames)
            {
                LogDebug($"PluginLevelMonitor: ⚠ plugin ring_frames={pluginFrames} != app {MicMeterSharedMemory.RingFrames}; restart the audio host so it reloads the bundled plugin build");
            }
            else
            {
                LogDebug($"PluginLevelMonitor: ring geometry ok (plugin ring_frames={(pluginFrames == 0 ? 65536 : pluginFrames)})");
            }
        }
    }

    public string DeviceId => "MicMeterMute";

    public string DeviceName => "MicMeter Mute";

    public bool IsRunning => !_disposed && _shared is not null;

    public bool IsListening => _listening;

    public bool IsMuted => _shared is not null ? _shared.ReadMute() : _muted;

    public float ConsumePeak() => _shared?.ReadPeak() ?? 0f;

    public void ToggleMute() => SetMute(!IsMuted);

    public void SetMute(bool muted)
    {
        _muted = muted;
        _shared?.WriteMute(muted);
    }

    /// <summary>
    /// Starts or stops playback of the plugin's audio on the configured output
    /// device. Returns true when the requested state was achieved.
    /// </summary>
    public bool SetListening(bool enabled)
    {
        if (_disposed || _shared is null)
        {
            _listening = false;
            return false;
        }

        if (enabled == _listening)
        {
            return _listening;
        }

        if (!enabled)
        {
            _audioListener?.Stop();
            _audioListener?.Dispose();
            _audioListener = null;
            _listening = false;
            return true;
        }

        var listener = _audioListener ??= new PluginSharedAudioListener(_shared, _outputDeviceUid);
        var result = listener.Start();
        _listening = result;
        if (!result)
        {
            _audioListener = null;
            listener.Dispose();
        }

        LogDebug($"SetListening: mode=Plugin state={_listening} outputUid={(string.IsNullOrWhiteSpace(_outputDeviceUid) ? "default" : _outputDeviceUid)}");
        return result;
    }

    internal void SetListeningOutputDevice(string? deviceId)
    {
        _outputDeviceUid = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        if (_listening)
        {
            _audioListener?.Stop();
            _audioListener?.Dispose();
            _audioListener = null;
            _listening = SetListening(true);
        }
    }

    /// <summary>Runs the listen output when the audio pull starts producing frames.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listening = false;
        _audioListener?.Dispose();
        _audioListener = null;
        _shared?.Dispose();
    }

    private static void LogDebug(string message)
    {
        try
        {
            System.IO.File.AppendAllText(
                "/tmp/micmeter_debug.log",
                DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
        }
        catch
        {
        }
    }
}