using System.Runtime.InteropServices;

namespace MicMeter.Audio;

/// <summary>
/// Mixes audio from multiple input monitors into a single stereo stream written
/// to the MicMeter virtual loopback device (macOS only). A single AUHAL output
/// unit is used; each source contributes its FloatRingBuffer via ReadLoopback,
/// summed into a stereo mix. Muted sources are excluded so per-device mute
/// silences that device's contribution without affecting the others.
/// </summary>
internal sealed class LoopbackMixer
{
    private const int SampleRate = 48000;
    private const int OutputChannels = 2;
    private const int MaxSourceChannels = 8;
    private const int MaxFramesPerCallback = 4096;
    private const int BytesPerSample = sizeof(float);

    private readonly object _gate = new();
    private readonly List<MacAudioMonitor> _sources = [];
    private readonly CoreAudio.AudioRenderCallback _outputCallback;
    private GCHandle _handle;
    private CoreAudio.AudioStreamBasicDescription _format;
    private IntPtr _outputUnit;
    private byte[] _outputBuffer;
    private float[] _mixBuffer;
    private float[] _scratchBuffer;
    private int _maxScratchSamples;
    private uint _deviceId;
    private bool _disposed;
    private int _bufferFrameSize;

    public LoopbackMixer(int bufferFrameSize = 0)
    {
        _bufferFrameSize = bufferFrameSize;
        _format = CoreAudio.CreateCanonicalFormat(SampleRate, OutputChannels);
        _outputBuffer = new byte[MaxFramesPerCallback * OutputChannels * BytesPerSample];
        _mixBuffer = new float[MaxFramesPerCallback * OutputChannels];
        _scratchBuffer = new float[MaxFramesPerCallback * MaxSourceChannels];
        _maxScratchSamples = _scratchBuffer.Length;
        _outputCallback = OutputCallback;
        _handle = GCHandle.Alloc(this);
    }

    public uint DeviceId => _deviceId;

    /// <summary>
    /// Starts (or restarts) the single output unit bound to the loopback device.
    /// Safe to call repeatedly; any prior output unit is torn down first.
    /// </summary>
    public bool Start(uint loopbackDeviceId)
    {
        lock (_gate)
        {
            StopCore();

            if (_disposed || loopbackDeviceId == 0)
            {
                return false;
            }

            try
            {
                _deviceId = loopbackDeviceId;
                // The loopback device must pass audio through at unity gain.
                CoreAudio.SetLoopbackDeviceToFullVolume(loopbackDeviceId);

                _outputUnit = CoreAudio.CreateOutputUnitForDevice(
                    loopbackDeviceId,
                    _format,
                    _outputCallback,
                    GCHandle.ToIntPtr(_handle),
                    _bufferFrameSize);

                if (_outputUnit == IntPtr.Zero || CoreAudio.StartUnit(_outputUnit) != 0)
                {
                    StopCore();
                    return false;
                }

                return true;
            }
            catch
            {
                StopCore();
                return false;
            }
        }
    }

    /// <summary>
    /// Replaces the set of monitors that contribute to the mix. Call from the
    /// UI thread. Sources are copied under the same lock read by the output
    /// callback; mutation happens while the mix is briefly stopped.
    /// </summary>
    public void SetSources(IReadOnlyList<MacAudioMonitor> sources)
    {
        lock (_gate)
        {
            _sources.Clear();
            foreach (var source in sources)
            {
                if (!source.IsDisposed)
                {
                    _sources.Add(source);
                }
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    private unsafe int OutputCallback(IntPtr refCon, ref uint flags, IntPtr timeStamp, uint busNumber, uint frames, IntPtr ioData)
    {
        var mixer = (LoopbackMixer)GCHandle.FromIntPtr(refCon).Target!;
        return mixer.Render(ref flags, frames, ioData);
    }

    private unsafe int Render(ref uint flags, uint frames, IntPtr ioData)
    {
        if (_disposed || ioData == IntPtr.Zero)
        {
            return -1;
        }

        var bufferList = (CoreAudio.AudioBufferList*)ioData;
        if (bufferList->NumberBuffers < 1 || bufferList->Buffer.Data == IntPtr.Zero)
        {
            return -1;
        }

        var frameCount = (int)Math.Min(frames, (uint)MaxFramesPerCallback);
        var sampleCount = frameCount * OutputChannels;
        Array.Clear(_mixBuffer, 0, sampleCount);

        // Snapshot contributing sources under the same gate: the callback is
        // realtime, so the list is only read; mutations happen while stopped.
        MacAudioMonitor[] sources;
        lock (_gate)
        {
            sources = _sources.ToArray();
        }

        foreach (var source in sources)
        {
            if (source.IsMuted || source.IsDisposed)
            {
                continue;
            }

            var srcChannels = Math.Max(1, source.Channels);
            var srcSamplesNeeded = frameCount * srcChannels;
            if (srcSamplesNeeded > _maxScratchSamples)
            {
                srcSamplesNeeded = _maxScratchSamples;
            }

            var scratch = _scratchBuffer.AsSpan(0, srcSamplesNeeded);
            scratch.Clear();
            source.ReadLoopback(scratch);

            // Downmix/upmix source to stereo and accumulate.
            if (srcChannels == 1)
            {
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var mono = scratch[frame];
                    _mixBuffer[frame * 2] += mono;
                    _mixBuffer[frame * 2 + 1] += mono;
                }
            }
            else
            {
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var left = scratch[frame * srcChannels];
                    var right = scratch[frame * srcChannels + 1];
                    _mixBuffer[frame * 2] += left;
                    _mixBuffer[frame * 2 + 1] += right;
                }
            }
        }

        var bytesNeeded = sampleCount * BytesPerSample;
        var byteSpan = MemoryMarshal.Cast<float, byte>(_mixBuffer.AsSpan(0, sampleCount));
        byteSpan.CopyTo(_outputBuffer.AsSpan(0, bytesNeeded));
        Marshal.Copy(_outputBuffer, 0, bufferList->Buffer.Data, bytesNeeded);
        bufferList->Buffer.DataByteSize = (uint)bytesNeeded;
        bufferList->Buffer.NumberChannels = OutputChannels;
        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }

    private void StopCore()
    {
        if (_outputUnit != IntPtr.Zero)
        {
            CoreAudio.DisposeUnit(_outputUnit);
            _outputUnit = IntPtr.Zero;
        }
    }
}
