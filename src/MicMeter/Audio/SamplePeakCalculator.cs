using System.Buffers.Binary;
using NAudio.Dmo;
using NAudio.Wave;

namespace MicMeter.Audio;

public static class SamplePeakCalculator
{
    public static float Calculate(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                      format is WaveFormatExtensible extensible &&
                      extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;

        return isFloat && format.BitsPerSample == 32
            ? CalculateFloat32(buffer)
            : format.BitsPerSample switch
            {
                8 => CalculatePcm8(buffer),
                16 => CalculatePcm16(buffer),
                24 => CalculatePcm24(buffer),
                32 => CalculatePcm32(buffer),
                _ => 0
            };
    }

    private static float CalculateFloat32(ReadOnlySpan<byte> buffer)
    {
        var peak = 0f;
        for (var offset = 0; offset + 4 <= buffer.Length; offset += 4)
        {
            var sample = Math.Abs(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..])));
            if (float.IsFinite(sample))
            {
                peak = Math.Max(peak, sample);
            }
        }

        return Math.Min(peak, 1f);
    }

    private static float CalculatePcm8(ReadOnlySpan<byte> buffer)
    {
        var peak = 0f;
        foreach (var value in buffer)
        {
            peak = Math.Max(peak, Math.Abs(value - 128) / 128f);
        }

        return peak;
    }

    private static float CalculatePcm16(ReadOnlySpan<byte> buffer)
    {
        var peak = 0f;
        for (var offset = 0; offset + 2 <= buffer.Length; offset += 2)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(buffer[offset..]);
            peak = Math.Max(peak, Math.Abs((int)value) / 32768f);
        }

        return peak;
    }

    private static float CalculatePcm24(ReadOnlySpan<byte> buffer)
    {
        var peak = 0f;
        for (var offset = 0; offset + 3 <= buffer.Length; offset += 3)
        {
            var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            peak = Math.Max(peak, Math.Abs((long)value) / 8388608f);
        }

        return peak;
    }

    private static float CalculatePcm32(ReadOnlySpan<byte> buffer)
    {
        var peak = 0f;
        for (var offset = 0; offset + 4 <= buffer.Length; offset += 4)
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);
            peak = Math.Max(peak, Math.Abs((long)value) / 2147483648f);
        }

        return peak;
    }
}
