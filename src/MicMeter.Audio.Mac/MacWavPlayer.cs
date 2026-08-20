using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MicMeter.Audio;

public sealed class MacWavPlayer : IDisposable
{
    private readonly byte[] _samples;
    private readonly int _channels;
    private readonly double _sampleRate;
    private readonly int _bitsPerSample;
    private readonly int _bytesPerFrame;
    private readonly CoreAudio.AudioRenderCallback _callback;
    private readonly GCHandle _handle;
    private IntPtr _unit;
    private int _position;
    private bool _finished;

    public MacWavPlayer(string path)
    {
        (_samples, _channels, _sampleRate, _bitsPerSample) = ReadWav(path);
        _bytesPerFrame = _channels * ((_bitsPerSample + 7) / 8);
        _callback = OutputCallback;
        _handle = GCHandle.Alloc(this);
    }

    public void Play()
    {
        _ = Task.Run(() =>
        {
            try
            {
                PlayCore();
            }
            catch
            {
                // Sound output is best-effort and must not affect mute control.
            }
        });
    }

    public void Dispose()
    {
        if (_unit != IntPtr.Zero)
        {
            CoreAudio.DisposeUnit(_unit);
            _unit = IntPtr.Zero;
        }

        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }

    private void PlayCore()
    {
        if (_samples.Length == 0)
        {
            return;
        }

        _position = 0;
        _finished = false;
        var format = CoreAudio.CreatePcmFormat(_sampleRate, _channels, _bitsPerSample);
        _unit = CoreAudio.CreateOutputUnit(CoreAudio.GetDefaultOutputDeviceId(), format, _callback, GCHandle.ToIntPtr(_handle));
        if (_unit == IntPtr.Zero || CoreAudio.StartUnit(_unit) != 0)
        {
            return;
        }

        var durationSeconds = _samples.Length / (double)_bytesPerFrame / _sampleRate;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(durationSeconds) + TimeSpan.FromMilliseconds(300);
        while (DateTime.UtcNow < deadline && !_finished)
        {
            Thread.Sleep(20);
        }

        if (_unit != IntPtr.Zero)
        {
            CoreAudio.DisposeUnit(_unit);
            _unit = IntPtr.Zero;
        }
    }

    private int OutputCallback(IntPtr refCon, ref uint flags, IntPtr timeStamp, uint busNumber, uint frames, IntPtr ioData)
    {
        var player = (MacWavPlayer)GCHandle.FromIntPtr(refCon).Target!;
        return player.RenderOutput(ref flags, timeStamp, busNumber, frames, ioData);
    }

    private unsafe int RenderOutput(ref uint flags, IntPtr timeStamp, uint busNumber, uint frames, IntPtr ioData)
    {
        if (_unit == IntPtr.Zero || ioData == IntPtr.Zero)
        {
            return -1;
        }

        var bufferList = (CoreAudio.AudioBufferList*)ioData;
        if (bufferList->NumberBuffers < 1 || bufferList->Buffer.Data == IntPtr.Zero)
        {
            return -1;
        }

        var requested = checked((int)(frames * _bytesPerFrame));
        var remaining = _samples.Length - _position;
        var toCopy = Math.Min(requested, remaining);
        if (toCopy > 0)
        {
            Marshal.Copy(_samples, _position, bufferList->Buffer.Data, toCopy);
            _position += toCopy;
        }

        if (requested > toCopy)
        {
            var silence = new byte[requested - toCopy];
            Marshal.Copy(silence, 0, IntPtr.Add(bufferList->Buffer.Data, toCopy), requested - toCopy);
        }

        bufferList->Buffer.DataByteSize = (uint)requested;
        bufferList->Buffer.NumberChannels = (uint)_channels;
        if (_position >= _samples.Length)
        {
            _finished = true;
        }

        return 0;
    }

    private static (byte[] samples, int channels, double sampleRate, int bitsPerSample) ReadWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var channels = 1;
        var sampleRate = 44100.0;
        var bitsPerSample = 16;
        byte[] samples = [];
        var offset = 12;

        while (offset + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, offset, 4);
            var chunkSize = BitConverter.ToInt32(bytes, offset + 4);
            var dataStart = offset + 8;
            if (dataStart < 0 || dataStart + chunkSize > bytes.Length)
            {
                break;
            }

            switch (chunkId)
            {
                case "fmt ":
                    channels = BitConverter.ToInt16(bytes, dataStart + 2);
                    sampleRate = BitConverter.ToInt32(bytes, dataStart + 4);
                    bitsPerSample = BitConverter.ToInt16(bytes, dataStart + 14);
                    break;
                case "data":
                    samples = new byte[chunkSize];
                    Array.Copy(bytes, dataStart, samples, 0, chunkSize);
                    break;
            }

            offset = dataStart + chunkSize + (chunkSize & 1);
        }

        return (samples, Math.Clamp(channels, 1, 8), sampleRate, bitsPerSample);
    }
}
