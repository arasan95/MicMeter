namespace MicMeter.Audio;

internal sealed class RingBuffer
{
    private readonly byte[] _buffer;
    private readonly object _gate = new();
    private int _readPos;
    private int _writePos;
    private int _available;

    public RingBuffer(int capacity)
    {
        _buffer = new byte[Math.Max(1, capacity)];
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            var count = Math.Min(data.Length, _buffer.Length - _available);
            if (count <= 0)
            {
                _available = 0;
                _readPos = 0;
                _writePos = 0;
                count = Math.Min(data.Length, _buffer.Length);
            }

            for (var index = 0; index < count; index++)
            {
                _buffer[_writePos] = data[index];
                _writePos = (_writePos + 1) % _buffer.Length;
            }

            _available += count;
        }
    }

    public int Read(Span<byte> destination)
    {
        lock (_gate)
        {
            var count = Math.Min(destination.Length, _available);
            for (var index = 0; index < count; index++)
            {
                destination[index] = _buffer[_readPos];
                _readPos = (_readPos + 1) % _buffer.Length;
            }

            _available -= count;
            return count;
        }
    }
}
