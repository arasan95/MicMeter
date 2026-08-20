namespace MicMeter.Audio;

public interface IAudioMonitor : IDisposable
{
    string DeviceId { get; }

    string DeviceName { get; }

    bool IsRunning { get; }

    bool IsListening { get; }

    bool IsMuted { get; }

    float ConsumePeak();

    void ToggleMute();

    void SetMute(bool muted);

    bool SetListening(bool enabled);
}
