namespace MicMeter.Audio;

public enum AudioSampleEncoding
{
    Pcm,
    IeeeFloat
}

public sealed record AudioFormat(
    int SampleRate,
    int BitsPerSample,
    int Channels,
    AudioSampleEncoding Encoding);
