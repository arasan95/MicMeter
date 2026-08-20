using MicMeter.Models;

namespace MicMeter.Audio;

public interface IAudioService : IDisposable
{
    IReadOnlyList<IAudioMonitor> Monitors { get; }

    IReadOnlyList<AudioDeviceInfo> GetCaptureDevices();

    /// <summary>
    /// Lists playback (output) devices usable for microphone listening.
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();

    IReadOnlyList<string> Connect(IReadOnlyCollection<string> preferredDeviceIds);

    void ToggleMute(string deviceId);

    bool ToggleListening(string deviceId);

    /// <summary>
    /// Selects the output device used for listening playback. An empty/null
    /// value routes to the system default output device.
    /// </summary>
    void SetListeningOutputDevice(string? deviceId);

    void ToggleMuteAll();

    void SetMuteAll(bool muted);

    /// <summary>
    /// Sets which input devices route into the MicMeter virtual loopback device
    /// (macOS only). Windows implementations are no-ops.
    /// </summary>
    void SetLoopbackDevices(IReadOnlyCollection<string> deviceIds);

    /// <summary>
    /// Sets the AUHAL buffer frame size used for capture and loopback output.
    /// 0 = hardware default. Call before Connect. Windows is a no-op.
    /// </summary>
    void SetBufferFrameSize(int bufferFrameSize);

    /// <summary>
    /// Selects the audio routing mode (loopback virtual device vs LV2 plugin).
    /// Call before Connect. Windows is a no-op.
    /// </summary>
    void SetRoutingMode(AudioRoutingMode mode);
}
