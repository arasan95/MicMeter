using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave;

namespace MicMeter.Audio;

public sealed class WindowsAudioMonitor : IAudioMonitor
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private int _peakBits;
    private int _running = 1;
    private bool _disposed;
    private readonly object _listeningGate = new();
    private BufferedWaveProvider? _listeningBuffer;
    private WasapiOut? _listeningOutput;
    private MediaFoundationResampler? _listeningResampler;
    private MMDeviceEnumerator? _renderEnumerator;
    private MMDevice? _renderDevice;
    private string? _listeningOutputDeviceId;

    public WindowsAudioMonitor(MMDevice device)
    {
        _device = device;
        DeviceId = device.ID;
        DeviceName = device.FriendlyName;
        _capture = new WasapiCapture(device, true, 50);
        _capture.ShareMode = AudioClientShareMode.Shared;
        _capture.DataAvailable += Capture_DataAvailable;
        _capture.RecordingStopped += Capture_RecordingStopped;
        try
        {
            _capture.StartRecording();
        }
        catch
        {
            _capture.DataAvailable -= Capture_DataAvailable;
            _capture.RecordingStopped -= Capture_RecordingStopped;
            _capture.Dispose();
            _device.Dispose();
            throw;
        }
    }

    public string DeviceId { get; }
    public string DeviceName { get; }
    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public bool IsListening
    {
        get
        {
            lock (_listeningGate)
            {
                return _listeningOutput is not null;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            try
            {
                return _device.AudioEndpointVolume.Mute;
            }
            catch
            {
                Volatile.Write(ref _running, 0);
                return false;
            }
        }
    }

    public float ConsumePeak() => BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _peakBits, 0));

    public void ToggleMute() => SetMute(!IsMuted);

    public void SetMute(bool muted)
    {
        try
        {
            _device.AudioEndpointVolume.Mute = muted;
        }
        catch
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public bool SetListening(bool enabled)
    {
        lock (_listeningGate)
        {
            StopListeningCore();
            if (!enabled || _disposed)
            {
                return false;
            }

            try
            {
                _renderEnumerator = new MMDeviceEnumerator();
                if (_listeningOutputDeviceId is { Length: > 0 })
                {
                    _renderDevice = _renderEnumerator.GetDevice(_listeningOutputDeviceId);
                    if (_renderDevice is null || _renderDevice.State != DeviceState.Active)
                    {
                        _renderDevice?.Dispose();
                        _renderDevice = null;
                        _renderDevice = _renderEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                }
                else
                {
                    _renderDevice = _renderEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }

                _listeningBuffer = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(2),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };
                _listeningResampler = new MediaFoundationResampler(
                    _listeningBuffer,
                    _renderDevice.AudioClient.MixFormat)
                {
                    ResamplerQuality = 60
                };
                _listeningOutput = new WasapiOut(_renderDevice, AudioClientShareMode.Shared, true, 100);
                _listeningOutput.Init(_listeningResampler);
                return true;
            }
            catch
            {
                StopListeningCore();
                return false;
            }
        }
    }

    internal void SetListeningOutputDevice(string? deviceId)
    {
        _listeningOutputDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        var peak = SamplePeakCalculator.Calculate(e.Buffer.AsSpan(0, e.BytesRecorded), ToAudioFormat(_capture.WaveFormat));
        PublishPeak(peak);

        lock (_listeningGate)
        {
            if (_listeningBuffer is null || _listeningOutput is null)
            {
                return;
            }

            try
            {
                _listeningBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                if (_listeningOutput.PlaybackState != PlaybackState.Playing &&
                    _listeningBuffer.BufferedDuration >= TimeSpan.FromMilliseconds(200))
                {
                    _listeningOutput.Play();
                }
            }
            catch
            {
                StopListeningCore();
            }
        }
    }

    private static AudioFormat ToAudioFormat(WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                      format is WaveFormatExtensible extensible &&
                      extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
        return new AudioFormat(
            format.SampleRate,
            format.BitsPerSample,
            format.Channels,
            isFloat ? AudioSampleEncoding.IeeeFloat : AudioSampleEncoding.Pcm);
    }

    private void PublishPeak(float peak)
    {
        peak = Math.Clamp(peak, 0, 1);
        while (true)
        {
            var currentBits = Volatile.Read(ref _peakBits);
            var current = BitConverter.Int32BitsToSingle(currentBits);
            if (current >= peak)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _peakBits, BitConverter.SingleToInt32Bits(peak), currentBits) == currentBits)
            {
                return;
            }
        }
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!_disposed)
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Volatile.Write(ref _running, 0);
        lock (_listeningGate)
        {
            StopListeningCore();
        }
        _capture.DataAvailable -= Capture_DataAvailable;
        _capture.RecordingStopped -= Capture_RecordingStopped;
        try
        {
            _capture.StopRecording();
        }
        catch
        {
            // The device may have disappeared already.
        }

        _capture.Dispose();
        _device.Dispose();
    }

    private void StopListeningCore()
    {
        _listeningBuffer = null;
        try
        {
            _listeningOutput?.Stop();
        }
        catch
        {
            // The render device may have disappeared.
        }

        _listeningOutput?.Dispose();
        _listeningOutput = null;
        _listeningResampler?.Dispose();
        _listeningResampler = null;
        _renderDevice?.Dispose();
        _renderDevice = null;
        _renderEnumerator?.Dispose();
        _renderEnumerator = null;
    }
}
