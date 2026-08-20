using System.IO;
#if WINDOWS
using System.Media;
#else
using MicMeter.Audio;
#endif

namespace MicMeter.Services;

public sealed class MuteSoundService : IDisposable
{
#if WINDOWS
    private readonly SoundPlayer? _mutePlayer;
    private readonly SoundPlayer? _unmutePlayer;
#else
    private readonly MacWavPlayer? _mutePlayer;
    private readonly MacWavPlayer? _unmutePlayer;
#endif

    public MuteSoundService()
    {
#if WINDOWS
        _mutePlayer = CreatePlayer("mute.wav");
        _unmutePlayer = CreatePlayer("unmute.wav");
#else
        _mutePlayer = CreateMacPlayer("mute.wav");
        _unmutePlayer = CreateMacPlayer("unmute.wav");
#endif
    }

    public void Play(bool muted)
    {
        try
        {
#if WINDOWS
            (muted ? _mutePlayer : _unmutePlayer)?.Play();
#else
            (muted ? _mutePlayer : _unmutePlayer)?.Play();
#endif
        }
        catch
        {
            // A missing or unavailable output device must not affect mute control.
        }
    }

    public void Dispose()
    {
        _mutePlayer?.Dispose();
        _unmutePlayer?.Dispose();
    }

#if WINDOWS
    private static SoundPlayer? CreatePlayer(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var player = new SoundPlayer(path);
            player.LoadAsync();
            return player;
        }
        catch
        {
            return null;
        }
    }
#else
    private static MacWavPlayer? CreateMacPlayer(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            return new MacWavPlayer(path);
        }
        catch
        {
            return null;
        }
    }
#endif
}
