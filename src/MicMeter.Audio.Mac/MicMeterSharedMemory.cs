using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MicMeter.Audio;

/// <summary>
/// POSIX named shared-memory block shared between the MicMeter app and the
/// MicMeter Mute LV2 plugin. Layout mirrors struct MicMeterShared in
/// plugins/MicMeterMute/micmeter_mute.c:
///
///     volatile float peak;          // plugin -> MicMeter (input peak, 0..1)
///     volatile int   mute;          // MicMeter -> plugin (0 = pass, 1 = silence)
///     volatile uint  sampleRate;    // plugin -> MicMeter (host sample rate)
///     volatile uint  writeFrame;    // plugin -> MicMeter (ring producer, release)
///     volatile uint  readFrame;     // MicMeter -> plugin (ring consumer)
///     float buffer[RingFrames * 2]; // interleaved stereo input frames
///
/// The shm_open/mmap calls are wrapped by libmicmeter_shm.dylib (see
/// plugins/MicMeterMute/micmeter_shm_helper.c), because macOS exports shm_open as
/// a variadic function and ftruncate with an $INODE64 symbol, both of which are
/// fragile to call directly from a C# P/Invoke.
/// </summary>
internal sealed unsafe class MicMeterSharedMemory : IDisposable
{
    private const string HelperName = "libmicmeter_shm.dylib";

    internal const int RingFrames = 65536;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Block
    {
        public float Peak;
        public int Mute;
        public uint SampleRate;
        public uint WriteFrame;
        public uint ReadFrame;
        public fixed float Buffer[RingFrames * 2];
        public uint PluginRingFrames;
    }

    private delegate IntPtr MapDelegate(out int fd);
    private delegate void UnmapDelegate(IntPtr ptr, int fd);

    private readonly IntPtr _library;
    private readonly MapDelegate _map;
    private readonly UnmapDelegate _unmap;
    private IntPtr _mapped;
    private int _fd = -1;
    private bool _disposed;

    internal IntPtr MapAddress => _mapped;

    private MicMeterSharedMemory(IntPtr library, MapDelegate map, UnmapDelegate unmap, IntPtr mapped, int fd)
    {
        _library = library;
        _map = map;
        _unmap = unmap;
        _mapped = mapped;
        _fd = fd;
    }

    public static MicMeterSharedMemory? Open()
    {
        void Log(string msg)
        {
            try { System.IO.File.AppendAllText("/tmp/micmeter_debug.log", DateTime.Now.ToString("HH:mm:ss.fff ") + msg + Environment.NewLine); }
            catch { /* ignore */ }
        }

        try
        {
            Log($"Open: BaseDirectory={AppContext.BaseDirectory}");

            // Load by absolute path: a bare name makes dlopen compare against the
            // dylib's install name (an absolute build path), which fails to match.
            var helperPath = Path.Combine(AppContext.BaseDirectory, HelperName);
            Log($"Open: helperPath={helperPath}");

            if (!NativeLibrary.TryLoad(helperPath, out var library))
            {
                Log($"Open FAIL: TryLoad({helperPath}) returned false");
                return null;
            }

            Log($"Open: TryLoad ok, library=0x{library:X}");

            if (!NativeLibrary.TryGetExport(library, "micmeter_shm_map", out var mapPtr) ||
                !NativeLibrary.TryGetExport(library, "micmeter_shm_unmap", out var unmapPtr))
            {
                Log("Open FAIL: TryGetExport failed");
                NativeLibrary.Free(library);
                return null;
            }

            var map = Marshal.GetDelegateForFunctionPointer<MapDelegate>(mapPtr);
            var unmap = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(unmapPtr);

            var mapped = map(out var fd);
            Log($"Open: micmeter_shm_map -> mapped=0x{mapped.ToInt64():X} fd={fd}");

            if (mapped == new IntPtr(-1) /* MAP_FAILED */ || mapped == IntPtr.Zero || fd < 0)
            {
                Log("Open FAIL: map returned MAP_FAILED");
                NativeLibrary.Free(library);
                return null;
            }

            Log("Open OK: shared memory mapped");
            return new MicMeterSharedMemory(library, map, unmap, mapped, fd);
        }
        catch (Exception ex)
        {
            Log($"Open FAIL: exception {ex.Message}");
            return null;
        }
    }

    public float ReadPeak()
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return 0f;
        }

        return ((Block*)_mapped)->Peak;
    }

    public bool ReadMute()
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return false;
        }

        return ((Block*)_mapped)->Mute != 0;
    }

    public void WriteMute(bool muted)
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return;
        }

        ((Block*)_mapped)->Mute = muted ? 1 : 0;
    }

    /// <summary>Host sample rate published by the plugin (0 when unknown).</summary>
    public uint ReadSampleRate()
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return 0;
        }

        return ((Block*)_mapped)->SampleRate;
    }

    /// <summary>Ring producer position (release-ordered by the writer).</summary>
    public uint ReadWriteFrame()
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return 0;
        }

        return Volatile.Read(ref ((Block*)_mapped)->WriteFrame);
    }

    /// <summary>Ring consumer position (owned by MicMeter).</summary>
    public uint ReadReadFrame()
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return 0;
        }

        return Volatile.Read(ref ((Block*)_mapped)->ReadFrame);
    }

    public void WriteReadFrame(uint frame)
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return;
        }

        Volatile.Write(ref ((Block*)_mapped)->ReadFrame, frame);
    }

    /// <summary>
    /// Ring geometry published by the plugin (written at instantiation). 0 means
    /// an older plugin is production that predates the field; the geometry is
    /// fixed at 65536 in that case too. When non-zero and different from
    /// <see cref="RingFrames"/>, the running host is using an outdated plugin
    /// build and must be restarted to pick up the bundled one.
    /// </summary>
    public uint ReadRingFrames()
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return 0;
        }

        return ((Block*)_mapped)->PluginRingFrames;
    }

    /// <summary>Reads one interleaved stereo sample: index = frame * 2 + channel.</summary>
    public float ReadBufferSample(int index)
    {
        if (_disposed || _mapped == IntPtr.Zero)
        {
            return 0f;
        }

        return ((Block*)_mapped)->Buffer[index];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mapped != IntPtr.Zero)
        {
            _unmap(_mapped, _fd);
            _mapped = IntPtr.Zero;
        }

        _fd = -1;

        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
        }
    }
}
