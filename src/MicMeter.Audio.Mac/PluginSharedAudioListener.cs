using System;
using System.Runtime.InteropServices;

namespace MicMeter.Audio;

/// <summary>
/// Plays back the audio that the MicMeter Mute LV2 plugin is processing by
/// pulling the plugin's shared-memory ring buffer and streaming it to an
/// output device (the system default, or a device selected in Settings).
/// Used by the Plugin routing mode's "listen" control; it needs no microphone
/// capture, so it is unaffected by input-capture permission or device issues.
/// </summary>
internal sealed unsafe class PluginSharedAudioListener : IDisposable
{
    private const int MaxFramesPerCallback = 8192;

    /// <summary>
    /// Target look-ahead when resetting the ring to its live edge on start.
    /// Small enough to keep monitor latency low, large enough to ride over
    /// normal host block jitter without underrunning on the very first callbacks.
    /// </summary>
    private const uint SlipFrames = 2048;

    private readonly MicMeterSharedMemory _shared;
    private readonly string? _outputDeviceUid;
    private readonly uint _sampleRate;
    private readonly uint _bytesPerFrame;
    private readonly GCHandle _handle;
    private readonly CoreAudio.AudioRenderCallback _callback;
    private readonly float[] _scratch;
    private IntPtr _outputUnit;
    private bool _disposed;

    public bool IsRunning => !_disposed && _outputUnit != IntPtr.Zero;

    public PluginSharedAudioListener(MicMeterSharedMemory shared, string? outputDeviceUid)
    {
        _shared = shared;
        _outputDeviceUid = outputDeviceUid;

        var rate = shared.ReadSampleRate();
        _sampleRate = rate >= 8000 && rate <= 192000 ? rate : 48000;

        var format = CoreAudio.CreateCanonicalFormat(_sampleRate, 2);
        _bytesPerFrame = format.BytesPerFrame;

        _handle = GCHandle.Alloc(this);
        _callback = OutputCallback;
        _scratch = new float[MaxFramesPerCallback * 2];
    }

    /// <summary>
    /// Starts an output unit for the configured device and begins draining the
    /// ring. Returns false when the output unit could not be created or started.
    /// </summary>
    public bool Start()
    {
        Stop();

        // When listening is enabled, jump to the live edge of the ring. If the
        // producer (the host plugin) has been running for a while, the ring
        // already holds a full backlog; without this reset the listener would
        // sit at the back of a saturated ring while the producer's overflow
        // drop cuts the oldest audio on every block (audible as chopping /
        // clicks). Keeping a small slip also bounds the monitor latency.
        var write = _shared.ReadWriteFrame();
        var read = _shared.ReadReadFrame();
        var used = (uint)(write - read);
        if (used > SlipFrames)
        {
            _shared.WriteReadFrame(write - SlipFrames);
        }

        try { System.IO.File.AppendAllText("/tmp/micmeter_debug.log", DateTime.Now.ToString("HH:mm:ss.fff ") + $"Listener.Start: ring used={used} (slip={SlipFrames}, rate={_sampleRate})" + Environment.NewLine); }
        catch { }

        var format = CoreAudio.CreateCanonicalFormat(_sampleRate, 2);
        var refCon = GCHandle.ToIntPtr(_handle);

        var deviceId = 0u;
        if (!string.IsNullOrWhiteSpace(_outputDeviceUid))
        {
            deviceId = CoreAudio.ResolveOutputDeviceId(_outputDeviceUid);
        }

        _outputUnit = deviceId != 0
            ? CoreAudio.CreateOutputUnitForDevice(deviceId, format, _callback, refCon)
            : CoreAudio.CreateOutputUnit(deviceId, format, _callback, refCon);

        if (_outputUnit == IntPtr.Zero)
        {
            return false;
        }

        if (CoreAudio.StartUnit(_outputUnit) != 0)
        {
            CoreAudio.DisposeUnit(_outputUnit);
            _outputUnit = IntPtr.Zero;
            return false;
        }

        return true;
    }

    public void Stop()
    {
        if (_outputUnit != IntPtr.Zero)
        {
            CoreAudio.DisposeUnit(_outputUnit);
            _outputUnit = IntPtr.Zero;
        }
    }

    private int OutputCallback(IntPtr refCon, ref uint flags, IntPtr timeStamp, uint busNumber, uint frames, IntPtr ioData)
    {
        if (_disposed || _outputUnit == IntPtr.Zero || ioData == IntPtr.Zero || frames == 0)
        {
            return -1;
        }

        var bufferList = (CoreAudio.AudioBufferList*)ioData;
        if (bufferList->NumberBuffers < 1 || bufferList->Buffer.Data == IntPtr.Zero)
        {
            return -1;
        }

        var renderFrames = Math.Min(frames, (uint)MaxFramesPerCallback);
        var write = _shared.ReadWriteFrame();
        var read = _shared.ReadReadFrame();
        var available = write - read;
        var copyFrames = Math.Min(renderFrames, available);

        var scratch = _scratch;
        var mask = MicMeterSharedMemory.RingFrames - 1;
        for (var i = 0; i < (int)copyFrames; i++)
        {
            var index = (int)((read + (uint)i) & mask) * 2;
            scratch[i * 2] = _shared.ReadBufferSample(index);
            scratch[i * 2 + 1] = _shared.ReadBufferSample(index + 1);
        }

        if (copyFrames < renderFrames)
        {
            Array.Clear(scratch, (int)copyFrames * 2, (int)(renderFrames - copyFrames) * 2);
        }

        if (copyFrames > 0)
        {
            _shared.WriteReadFrame(read + copyFrames);
        }

        Marshal.Copy(scratch, 0, bufferList->Buffer.Data, (int)renderFrames * 2);
        bufferList->Buffer.DataByteSize = renderFrames * _bytesPerFrame;
        bufferList->Buffer.NumberChannels = 2;
        return 0;
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _handle.Free();
    }
}