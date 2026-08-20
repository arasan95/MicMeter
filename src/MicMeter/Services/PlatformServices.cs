using MicMeter.Audio;

namespace MicMeter.Services;

public static class PlatformServices
{
    public static IAudioService CreateAudioService()
    {
#if WINDOWS
        return new WindowsAudioService();
#else
        return new MacAudioService();
#endif
    }
}
