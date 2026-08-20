using System.Runtime.InteropServices;

namespace MicMeter.Audio;

public sealed class MacAudioMonitor : IAudioMonitor
{
    private enum MuteStrategy
    {
        OsMute,
        VirtualVolume,
        Volume,
        App
    }

    private readonly uint _deviceId;
    private readonly int _channels;
    private readonly int _bufferFrameSize;
    private string? _listeningOutputDeviceUid;
    private readonly MuteStrategy _muteStrategy;
    private readonly float[] _originalVolumes;
    private float _savedVirtualVolume = 0.5f;
    private readonly CoreAudio.AudioRenderCallback _inputCallback;
    private readonly GCHandle _handle;
    private readonly object _listenGate = new();
    private IntPtr _unit;
    private CoreAudio.AudioStreamBasicDescription _format;
    private AudioFormat? _audioFormat;
    private IntPtr _dataPtr;
    private byte[] _managedBuffer = [];
    private byte[] _outputBuffer = [];
    private int _bufferSize;
    private int _peakBits;
    private int _running = 1;
    private bool _disposed;
    private bool _volumeMuted;
    private bool _appMuted;
    private IntPtr _outputUnit;
    private CoreAudio.AudioRenderCallback? _outputCallback;
    private RingBuffer? _ringBuffer;
    private int _outputCallbacks;
    private int _outputWithData;
    private int _outputEmpty;
    private int _inputCallbacks;
    private int _inputWithData;
    private long _inputBytes;
    private readonly FloatRingBuffer _loopbackBuffer;

    public MacAudioMonitor(string uid, int bufferFrameSize = 0)
    {
        DeviceId = uid;
        _bufferFrameSize = bufferFrameSize;
        _deviceId = CoreAudio.ResolveDeviceId(uid);
        if (_deviceId == 0)
        {
            throw new InvalidOperationException($"Audio device not found: {uid}");
        }

        var device = CoreAudio.GetInputDevices().FirstOrDefault(item => item.ObjectId == _deviceId);
        DeviceName = device?.Name ?? uid;
        _channels = Math.Clamp(CoreAudio.GetInputChannelCount(_deviceId), 1, 8);
        _originalVolumes = new float[_channels];
        _loopbackBuffer = new FloatRingBuffer(48000 * _channels);

        if (CoreAudio.CanControlMute(_deviceId))
        {
            _muteStrategy = MuteStrategy.OsMute;
        }
        else if (CoreAudio.CanControlVirtualVolume(_deviceId))
        {
            _muteStrategy = MuteStrategy.VirtualVolume;
            _savedVirtualVolume = CoreAudio.GetVirtualVolume(_deviceId);
            if (_savedVirtualVolume <= 0.01f)
            {
                _savedVirtualVolume = 0.5f;
            }
        }
        else if (CoreAudio.CanControlVolume(_deviceId, 0))
        {
            _muteStrategy = MuteStrategy.Volume;
            for (var channel = 0; channel < _channels; channel++)
            {
                _originalVolumes[channel] = CoreAudio.GetVolume(_deviceId, channel);
            }
        }
        else
        {
            _muteStrategy = MuteStrategy.App;
        }

        _inputCallback = InputCallback;
        _handle = GCHandle.Alloc(this);
        _unit = CoreAudio.CreateInputUnit(
            _deviceId,
            _channels,
            48000,
            _inputCallback,
            GCHandle.ToIntPtr(_handle),
            out _format,
            bufferFrameSize);

        if (_unit == IntPtr.Zero)
        {
            _running = 0;
            return;
        }

        _audioFormat = CoreAudio.ToAudioFormat(_format);
        _bufferSize = Math.Max(64 * 1024, checked((int)(_format.BytesPerFrame * 4096)));
        _dataPtr = Marshal.AllocHGlobal(_bufferSize);
        _managedBuffer = new byte[_bufferSize];
        _outputBuffer = new byte[_bufferSize];

        if (CoreAudio.StartUnit(_unit) != 0)
        {
            _running = 0;
        }
    }

    public string DeviceId { get; }
    public string DeviceName { get; }
    public int Channels => _channels;
    public bool IsRunning => Volatile.Read(ref _running) == 1;
    public bool IsDisposed => Volatile.Read(ref _disposed);

    public bool IsListening
    {
        get
        {
            lock (_listenGate)
            {
                return _outputUnit != IntPtr.Zero;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            if (CoreAudio.CanControlMute(_deviceId) && GetOsMute())
            {
                return true;
            }

            if (CoreAudio.CanControlVirtualVolume(_deviceId))
            {
                var vol = CoreAudio.GetVirtualVolume(_deviceId);
                if (vol <= 0.001f)
                {
                    return true;
                }
            }

            return _muteStrategy switch
            {
                MuteStrategy.OsMute => GetOsMute(),
                MuteStrategy.VirtualVolume => _volumeMuted,
                MuteStrategy.Volume => _volumeMuted,
                _ => _appMuted
            };
        }
    }

    public float ConsumePeak() => BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _peakBits, 0));

    public int ReadLoopback(Span<float> destination) => _loopbackBuffer.Read(destination);

    public void ToggleMute()
    {
        FineTuneClient.ToggleInputMute(DeviceId);
        SetMute(!IsMuted);
    }

    public void SetMute(bool muted)
    {
        FineTuneClient.SetInputMute(muted, DeviceId);

        switch (_muteStrategy)
        {
            case MuteStrategy.OsMute:
                try
                {
                    CoreAudio.SetMute(_deviceId, muted);
                }
                catch
                {
                    Volatile.Write(ref _running, 0);
                }
                break;

            case MuteStrategy.VirtualVolume:
                if (muted)
                {
                    var current = CoreAudio.GetVirtualVolume(_deviceId);
                    if (current > 0.01f)
                    {
                        _savedVirtualVolume = current;
                    }
                    CoreAudio.SetVirtualVolume(_deviceId, 0f);
                    _volumeMuted = true;
                }
                else
                {
                    var restore = _savedVirtualVolume > 0.01f ? _savedVirtualVolume : 0.5f;
                    CoreAudio.SetVirtualVolume(_deviceId, restore);
                    _volumeMuted = false;
                }
                break;

            case MuteStrategy.Volume:
                if (muted && !_volumeMuted)
                {
                    for (var channel = 0; channel < _channels; channel++)
                    {
                        _originalVolumes[channel] = CoreAudio.GetVolume(_deviceId, channel);
                    }

                    for (var channel = 0; channel < _channels; channel++)
                    {
                        CoreAudio.SetVolume(_deviceId, channel, 0);
                    }

                    _volumeMuted = true;
                }
                else if (!muted && _volumeMuted)
                {
                    for (var channel = 0; channel < _channels; channel++)
                    {
                        CoreAudio.SetVolume(_deviceId, channel, _originalVolumes[channel]);
                    }

                    _volumeMuted = false;
                }
                break;

            default:
                _appMuted = muted;
                break;
        }
    }

    public bool SetListening(bool enabled)
    {
        lock (_listenGate)
        {
            StopListeningCore();
            if (!enabled || _disposed || _unit == IntPtr.Zero)
            {
                LogDebug($"SetListening: device={DeviceId} enabled={enabled} skipped disposed={_disposed} inputUnit={(long)_unit:X}");
                return false;
            }

            try
            {
                _ringBuffer = new RingBuffer(Math.Max(64 * 1024, _bufferSize * 4));
                _outputCallback = OutputCallback;
                var explicitOutput = _listeningOutputDeviceUid is { Length: > 0 };
                var outputDeviceId = CoreAudio.ResolveOutputDeviceId(_listeningOutputDeviceUid);
                _outputUnit = explicitOutput
                    ? CoreAudio.CreateOutputUnitForDevice(outputDeviceId, _format, _outputCallback, GCHandle.ToIntPtr(_handle), _bufferFrameSize)
                    : CoreAudio.CreateOutputUnit(outputDeviceId, _format, _outputCallback, GCHandle.ToIntPtr(_handle));
                var startCode = _outputUnit != IntPtr.Zero ? CoreAudio.StartUnit(_outputUnit) : int.MaxValue;
                var outputName = outputDeviceId != 0
                    ? CoreAudio.GetOutputDevices().FirstOrDefault(device => device.ObjectId == outputDeviceId)?.Name ?? "?"
                    : "?";
                LogDebug($"SetListening: device={DeviceId} enabled={enabled} channels={_channels} bufSize={_bufferSize} explicitOutput={explicitOutput} outputDeviceId={outputDeviceId} outputName={outputName} unit={(long)_outputUnit:X} startCode={startCode}");
                if (_outputUnit == IntPtr.Zero || startCode != 0)
                {
                    StopListeningCore();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogDebug($"SetListening: device={DeviceId} enabled={enabled} EXCEPTION {ex}");
                StopListeningCore();
                return false;
            }
        }
    }

    internal void SetListeningOutputDevice(string? uid)
    {
        _listeningOutputDeviceUid = string.IsNullOrWhiteSpace(uid) ? null : uid;
    }

    private bool GetOsMute()
    {
        try
        {
            return CoreAudio.GetMute(_deviceId);
        }
        catch
        {
            Volatile.Write(ref _running, 0);
            return false;
        }
    }

    private int InputCallback(IntPtr refCon, ref uint flags, IntPtr timeStamp, uint busNumber, uint frames, IntPtr ioData)
    {
        var monitor = (MacAudioMonitor)GCHandle.FromIntPtr(refCon).Target!;
        return monitor.RenderInput(ref flags, timeStamp, busNumber, frames, ioData);
    }

    private unsafe int RenderInput(ref uint flags, IntPtr timeStamp, uint busNumber, uint frames, IntPtr ioData)
    {
        if (_disposed || _unit == IntPtr.Zero || _dataPtr == IntPtr.Zero)
        {
            return -1;
        }

        var byteSize = Math.Min(_bufferSize, checked((int)(frames * _format.BytesPerFrame)));
        var bufferList = new CoreAudio.AudioBufferList
        {
            NumberBuffers = 1,
            Buffer = new CoreAudio.AudioBuffer
            {
                NumberChannels = _format.ChannelsPerFrame,
                DataByteSize = (uint)byteSize,
                Data = _dataPtr
            }
        };

        var status = CoreAudio.Render(_unit, ref flags, timeStamp, busNumber, frames, &bufferList);
        if (status == 0 && bufferList.Buffer.DataByteSize > 0)
        {
            var bytes = (int)bufferList.Buffer.DataByteSize;
            Marshal.Copy(_dataPtr, _managedBuffer, 0, bytes);
            _loopbackBuffer.Write(MemoryMarshal.Cast<byte, float>(_managedBuffer.AsSpan(0, bytes)));
            if (_audioFormat is not null)
            {
                PublishPeak(SamplePeakCalculator.Calculate(_managedBuffer.AsSpan(0, bytes), _audioFormat));
            }
            lock (_listenGate)
            {
                _ringBuffer?.Write(_managedBuffer.AsSpan(0, bytes));
            }
        }
        else if (status != 0)
        {
            Volatile.Write(ref _running, 0);
        }

        _inputCallbacks++;
        if (status == 0 && bufferList.Buffer.DataByteSize > 0) _inputWithData++;
        _inputBytes += bufferList.Buffer.DataByteSize;
        if (_inputCallbacks >= 100)
        {
            LogDebug($"RenderInput: device={DeviceId} callbacks={_inputCallbacks} withData={_inputWithData} bytes={_inputBytes} running={Volatile.Read(ref _running)}");
            _inputCallbacks = 0;
            _inputWithData = 0;
            _inputBytes = 0;
        }

        return status;
    }

    private int OutputCallback(IntPtr refCon, ref uint flags, IntPtr timeStamp, uint busNumber, uint frames, IntPtr ioData)
    {
        var monitor = (MacAudioMonitor)GCHandle.FromIntPtr(refCon).Target!;
        return monitor.RenderOutput(ref flags, timeStamp, busNumber, frames, ioData);
    }

    private unsafe int RenderOutput(ref uint flags, IntPtr timeStamp, uint busNumber, uint frames, IntPtr ioData)
    {
        if (_disposed || _outputUnit == IntPtr.Zero || ioData == IntPtr.Zero)
        {
            return -1;
        }

        var bufferList = (CoreAudio.AudioBufferList*)ioData;
        if (bufferList->NumberBuffers < 1 || bufferList->Buffer.Data == IntPtr.Zero)
        {
            return -1;
        }

        var requested = Math.Min(_bufferSize, checked((int)(frames * _format.BytesPerFrame)));
        Array.Clear(_outputBuffer, 0, requested);
        lock (_listenGate)
        {
            var readCount = _ringBuffer?.Read(_outputBuffer.AsSpan(0, requested)) ?? 0;
            if (readCount > 0) _outputWithData++; else _outputEmpty++;
        }

        if (++_outputCallbacks >= 100)
        {
            LogDebug($"RenderOutput: device={DeviceId} withData={_outputWithData} empty={_outputEmpty}");
            _outputCallbacks = 0;
            _outputWithData = 0;
            _outputEmpty = 0;
        }

        Marshal.Copy(_outputBuffer, 0, bufferList->Buffer.Data, requested);
        bufferList->Buffer.DataByteSize = (uint)requested;
        bufferList->Buffer.NumberChannels = _format.ChannelsPerFrame;
        return 0;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Volatile.Write(ref _running, 0);
        lock (_listenGate)
        {
            StopListeningCore();
        }

        if (_unit != IntPtr.Zero)
        {
            CoreAudio.DisposeUnit(_unit);
            _unit = IntPtr.Zero;
        }

        if (_dataPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_dataPtr);
            _dataPtr = IntPtr.Zero;
        }

        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }

    private void StopListeningCore()
    {
        if (_outputUnit != IntPtr.Zero)
        {
            CoreAudio.DisposeUnit(_outputUnit);
            _outputUnit = IntPtr.Zero;
        }

        _outputCallback = null;
        _ringBuffer = null;
        _outputCallbacks = 0;
        _outputWithData = 0;
        _outputEmpty = 0;
    }

    private static void LogDebug(string message)
    {
        try { System.IO.File.AppendAllText("/tmp/micmeter_debug.log", DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine); }
        catch { }
    }
}
