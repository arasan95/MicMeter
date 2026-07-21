using System.Media;
using System.IO;

namespace MicMeter.Services;

public sealed class MuteSoundService : IDisposable
{
    private readonly SoundPlayer? _mutePlayer = CreatePlayer("mute.wav");
    private readonly SoundPlayer? _unmutePlayer = CreatePlayer("unmute.wav");

    public void Play(bool muted)
    {
        try
        {
            (muted ? _mutePlayer : _unmutePlayer)?.Play();
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
}
