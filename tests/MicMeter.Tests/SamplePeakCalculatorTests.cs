using System.Buffers.Binary;
using MicMeter.Audio;
using NAudio.Wave;

namespace MicMeter.Tests;

public sealed class SamplePeakCalculatorTests
{
    [Fact]
    public void Calculate_ReadsFloat32Peak()
    {
        var values = new[] { -0.25f, 0.75f, 0.1f };
        var bytes = new byte[values.Length * 4];
        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), BitConverter.SingleToInt32Bits(values[index]));
        }

        var peak = SamplePeakCalculator.Calculate(bytes, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        Assert.Equal(0.75f, peak, 3);
    }

    [Fact]
    public void Calculate_ReadsPcm16Peak()
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0), -8192);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), 16384);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 1024);

        var peak = SamplePeakCalculator.Calculate(bytes, new WaveFormat(48000, 16, 1));
        Assert.Equal(0.5f, peak, 3);
    }

    [Fact]
    public void Calculate_ReadsPcm24Peak()
    {
        byte[] bytes = [0x00, 0x00, 0xC0, 0x00, 0x00, 0x20];
        var peak = SamplePeakCalculator.Calculate(bytes, new WaveFormat(48000, 24, 1));
        Assert.Equal(0.5f, peak, 3);
    }

    [Fact]
    public void Calculate_ReadsPcm32Peak()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0), -1073741824);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 536870912);

        var peak = SamplePeakCalculator.Calculate(bytes, new WaveFormat(48000, 32, 1));
        Assert.Equal(0.5f, peak, 3);
    }
}
