namespace MicMeter.Audio;

// Single-producer, single-consumer float ring buffer. The producer is a
// capture AudioUnit's input callback and the consumer is the loopback output
// callback, so all access must stay lock-free and realtime-safe.
internal sealed class FloatRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _capacity;
    private long _readPos;
    private long _writePos;

    public FloatRingBuffer(int capacity)
    {
        _capacity = Math.Max(16, capacity);
        _buffer = new float[_capacity];
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return;
        }

        var count = Math.Min(samples.Length, _capacity);
        var write = Volatile.Read(ref _writePos);
        var read = Volatile.Read(ref _readPos);

        // On overflow, drop the oldest samples so the consumer stays aligned
        // with live audio instead of replaying stale data.
        if (count > _capacity - (write - read))
        {
            Volatile.Write(ref _readPos, write + count - _capacity);
            read = Volatile.Read(ref _readPos);
        }

        var start = (int)(write % _capacity);
        var first = Math.Min(count, _capacity - start);
        samples[..first].CopyTo(_buffer.AsSpan(start, first));
        if (count > first)
        {
            samples[first..count].CopyTo(_buffer.AsSpan(0, count - first));
        }

        Volatile.Write(ref _writePos, write + count);
    }

    public int Read(Span<float> destination)
    {
        var read = Volatile.Read(ref _readPos);
        var write = Volatile.Read(ref _writePos);
        var available = (int)Math.Min(write - read, int.MaxValue);
        var count = Math.Min(available, destination.Length);

        if (count > 0)
        {
            var start = (int)(read % _capacity);
            var first = Math.Min(count, _capacity - start);
            _buffer.AsSpan(start, first).CopyTo(destination[..first]);
            if (count > first)
            {
                _buffer.AsSpan(0, count - first).CopyTo(destination[first..count]);
            }

            Volatile.Write(ref _readPos, read + count);
        }

        if (count < destination.Length)
        {
            destination[count..].Clear();
        }

        return count;
    }
}
