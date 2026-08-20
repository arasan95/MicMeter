using System.Runtime.InteropServices;
using System.Text;

namespace MicMeter.Audio;

internal static class CoreAudio
{
    private const string CoreAudioDylib = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string AudioToolboxDylib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundationDylib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint KcfStringEncodingUtf8 = 0x08000100;

    private const uint SystemObject = 1;

    private const uint ScopeGlobal = 0x676C6F62; // 'glob'
    private const uint ScopeInput = 0x696E7074; // 'inpt'
    private const uint ScopeOutput = 0x6F757470; // 'outp'

    private const uint PropertyDevices = 0x64657623; // 'dev#'
    private const uint PropertyDefaultInputDevice = 0x64496E20; // 'dIn '
    private const uint PropertyDefaultOutputDevice = 0x644F7574; // 'dOut'
    private const uint PropertyDeviceUid = 0x75696420; // 'uid '
    private const uint PropertyDeviceName = 0x6C6E616D; // 'lnam'
    private const uint PropertyStreamConfiguration = 0x736C6179; // 'slay'
    private const uint PropertyStreams = 0x73746D23; // 'stm#'
    private const uint PropertyMute = 0x6D757465; // 'mute'
    private const uint PropertyVolumeScalar = 0x766F6C6D; // 'volm'
    private const uint PropertyVirtualMainVolume = 0x766D7663; // 'vmvc' (kAudioHardwareServiceDeviceProperty_VirtualMainVolume)
    private const uint PropertyNominalSampleRate = 0x6E737274; // 'nsrt'

    private const uint AudioFormatLinearPcm = 0x6C70636D; // 'lpcm'
    private const uint AudioFormatFlagIsFloat = 1 << 0;
    private const uint AudioFormatFlagIsSignedInteger = 1 << 2;
    private const uint AudioFormatFlagIsPacked = 1 << 3;

    private const uint AudioUnitTypeOutput = 0x61756F75; // 'auou'
    private const uint AudioUnitSubTypeHalOutput = 0x6168616C; // 'ahal'
    private const uint AudioUnitSubTypeDefaultOutput = 0x64656620; // 'def '
    private const uint AudioUnitManufacturerApple = 0x6170706C; // 'appl'

    private const uint AudioUnitScopeGlobal = 0;
    private const uint AudioUnitScopeInput = 1;
    private const uint AudioUnitScopeOutput = 2;

    private const uint PropertyStreamFormat = 8;
    private const uint PropertyEnableIo = 2003;
    private const uint PropertySetRenderCallback = 23;
    private const uint PropertySetInputCallback = 2005;
    private const uint PropertyCurrentDevice = 2000;
    private const uint PropertyMaximumFramesPerSlice = 14; // kAudioUnitProperty_MaximumFramesPerSlice

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioObjectPropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;

        public AudioObjectPropertyAddress(uint selector, uint scope, uint element = 0)
        {
            Selector = selector;
            Scope = scope;
            Element = element;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioBuffer
    {
        public uint NumberChannels;
        public uint DataByteSize;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioBufferList
    {
        public uint NumberBuffers;
        public AudioBuffer Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioComponentDescription
    {
        public uint ComponentType;
        public uint ComponentSubType;
        public uint ComponentManufacturer;
        public uint ComponentFlags;
        public uint ComponentFlagsMask;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int AudioRenderCallback(
        IntPtr inRefCon,
        ref uint ioActionFlags,
        IntPtr inTimeStamp,
        uint inBusNumber,
        uint inNumberFrames,
        IntPtr ioData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioRenderCallbackStruct
    {
        public IntPtr InputProc;
        public IntPtr InputProcRefCon;
    }

    internal sealed record DeviceInfo(uint ObjectId, string Uid, string Name);

    [DllImport(CoreAudioDylib)]
    private static extern int AudioObjectGetPropertyDataSize(
        uint objectId,
        ref AudioObjectPropertyAddress address,
        uint qualifierDataSize,
        IntPtr qualifierData,
        ref uint dataSize);

    [DllImport(CoreAudioDylib)]
    private static extern int AudioObjectGetPropertyData(
        uint objectId,
        ref AudioObjectPropertyAddress address,
        uint qualifierDataSize,
        IntPtr qualifierData,
        ref uint dataSize,
        IntPtr data);

    [DllImport(CoreAudioDylib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AudioObjectHasProperty(uint objectId, ref AudioObjectPropertyAddress address);

    [DllImport(CoreAudioDylib)]
    private static extern int AudioObjectIsPropertySettable(
        uint objectId,
        ref AudioObjectPropertyAddress address,
        [MarshalAs(UnmanagedType.I1)]
        out bool isSettable);

    [DllImport(CoreAudioDylib)]
    private static extern int AudioObjectSetPropertyData(
        uint objectId,
        ref AudioObjectPropertyAddress address,
        uint qualifierDataSize,
        IntPtr qualifierData,
        uint dataSize,
        IntPtr data);

    [DllImport(CoreFoundationDylib)]
    private static extern nint CFStringGetLength(IntPtr theString);

    [DllImport(CoreFoundationDylib)]
    private static extern bool CFStringGetCString(IntPtr theString, IntPtr buffer, nint bufferSize, uint encoding);

    [DllImport(AudioToolboxDylib)]
    private static extern IntPtr AudioComponentFindNext(IntPtr inComponent, ref AudioComponentDescription inDesc);

    [DllImport(AudioToolboxDylib)]
    private static extern int AudioComponentInstanceNew(IntPtr inComponent, out IntPtr outInstance);

    [DllImport(AudioToolboxDylib)]
    private static extern int AudioComponentInstanceDispose(IntPtr inInstance);

    [DllImport(AudioToolboxDylib)]
    private static extern int AudioUnitInitialize(IntPtr inUnit);

    [DllImport(AudioToolboxDylib)]
    private static extern int AudioUnitUninitialize(IntPtr inUnit);

    [DllImport(AudioToolboxDylib)]
    private static extern int AudioUnitSetProperty(
        IntPtr inUnit,
        uint inId,
        uint inScope,
        uint inElement,
        IntPtr inData,
        uint inDataSize);

    [DllImport(AudioToolboxDylib)]
    private static extern int AudioUnitGetProperty(
        IntPtr inUnit,
        uint inId,
        uint inScope,
        uint inElement,
        IntPtr outData,
        ref uint ioDataSize);

    [DllImport(AudioToolboxDylib)]
    private static extern int AudioOutputUnitStart(IntPtr inUnit);

    [DllImport(AudioToolboxDylib)]
    private static extern int AudioOutputUnitStop(IntPtr inUnit);

    [DllImport(AudioToolboxDylib)]
    private static extern unsafe int AudioUnitRender(
        IntPtr inUnit,
        ref uint ioActionFlags,
        IntPtr inTimeStamp,
        uint inOutputBusNumber,
        uint inNumberFrames,
        AudioBufferList* ioData);

    internal static unsafe int Render(
        IntPtr inUnit,
        ref uint ioActionFlags,
        IntPtr inTimeStamp,
        uint inOutputBusNumber,
        uint inNumberFrames,
        AudioBufferList* ioData) =>
        AudioUnitRender(inUnit, ref ioActionFlags, inTimeStamp, inOutputBusNumber, inNumberFrames, ioData);

    internal static IReadOnlyList<DeviceInfo> GetInputDevices()
    {
        var devices = GetPropertyData<uint>(SystemObject, new AudioObjectPropertyAddress(PropertyDevices, ScopeGlobal));
        var result = new List<DeviceInfo>();
        foreach (var objectId in devices)
        {
            try
            {
                var uid = GetStringProperty(objectId, new AudioObjectPropertyAddress(PropertyDeviceUid, ScopeGlobal));
                var name = GetStringProperty(objectId, new AudioObjectPropertyAddress(PropertyDeviceName, ScopeGlobal));
                var inputChannels = GetChannelCount(objectId, ScopeInput);
                if (string.IsNullOrWhiteSpace(uid) || inputChannels == 0)
                {
                    continue;
                }

                result.Add(new DeviceInfo(objectId, uid, string.IsNullOrWhiteSpace(name) ? uid : name));
            }
            catch
            {
                // Skip devices that cannot be inspected.
            }
        }

        return result;
    }

    internal static uint ResolveDeviceId(string uid)
    {
        var device = GetInputDevices().FirstOrDefault(item => item.Uid == uid);
        return device?.ObjectId ?? 0;
    }

    internal static IReadOnlyList<DeviceInfo> GetOutputDevices()
    {
        var devices = GetPropertyData<uint>(SystemObject, new AudioObjectPropertyAddress(PropertyDevices, ScopeGlobal));
        var result = new List<DeviceInfo>();
        foreach (var objectId in devices)
        {
            try
            {
                var uid = GetStringProperty(objectId, new AudioObjectPropertyAddress(PropertyDeviceUid, ScopeGlobal));
                var name = GetStringProperty(objectId, new AudioObjectPropertyAddress(PropertyDeviceName, ScopeGlobal));
                if (string.IsNullOrWhiteSpace(uid) || GetChannelCount(objectId, ScopeOutput) == 0)
                {
                    continue;
                }

                result.Add(new DeviceInfo(objectId, uid, string.IsNullOrWhiteSpace(name) ? uid : name));
            }
            catch
            {
                // Skip devices that cannot be inspected.
            }
        }

        return result;
    }

    internal static uint ResolveOutputDeviceId(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return GetDefaultOutputDeviceId();
        }

        var device = GetOutputDevices().FirstOrDefault(item => item.Uid == uid);
        return device?.ObjectId ?? GetDefaultOutputDeviceId();
    }

    internal static uint GetDefaultInputDeviceId()
    {
        var ids = GetPropertyData<uint>(SystemObject, new AudioObjectPropertyAddress(PropertyDefaultInputDevice, ScopeGlobal));
        return ids.Count > 0 ? ids[0] : 0;
    }

    internal static uint GetDefaultOutputDeviceId()
    {
        var ids = GetPropertyData<uint>(SystemObject, new AudioObjectPropertyAddress(PropertyDefaultOutputDevice, ScopeGlobal));
        return ids.Count > 0 ? ids[0] : 0;
    }

    internal static int GetChannelCount(uint deviceId, uint scope)
    {
        var configuration = GetPropertyDataRaw(deviceId, new AudioObjectPropertyAddress(PropertyStreamConfiguration, scope));
        if (configuration.Length < 4)
        {
            return 0;
        }

        var bufferCount = BitConverter.ToUInt32(configuration, 0);
        var channels = 0;
        // AudioBufferList layout: UInt32 mNumberBuffers, then 4 bytes of padding to
        // align the AudioBuffer array to an 8-byte boundary. Each AudioBuffer is
        // UInt32 channels, UInt32 byteSize, IntPtr data (16 bytes).
        var offset = 8;
        for (var index = 0; index < bufferCount && offset + 4 <= configuration.Length; index++)
        {
            channels += (int)BitConverter.ToUInt32(configuration, offset);
            offset += 16;
        }

        return channels;
    }

    internal static int GetInputChannelCount(uint deviceId) => GetChannelCount(deviceId, ScopeInput);

    internal static double GetNominalSampleRate(uint deviceId)
    {
        var values = GetPropertyData<double>(deviceId, new AudioObjectPropertyAddress(PropertyNominalSampleRate, ScopeGlobal));
        return values.Count > 0 ? values[0] : 48000.0;
    }

    internal static bool CanControlMute(uint deviceId)
    {
        var address = new AudioObjectPropertyAddress(PropertyMute, ScopeInput);
        return AudioObjectHasProperty(deviceId, ref address) &&
               AudioObjectIsPropertySettable(deviceId, ref address, out var settable) == 0 && settable;
    }

    internal static bool GetMute(uint deviceId)
    {
        var address = new AudioObjectPropertyAddress(PropertyMute, ScopeInput);
        var values = GetPropertyData<uint>(deviceId, address);
        return values.Count > 0 && values[0] != 0;
    }

    internal static void SetMute(uint deviceId, bool muted)
    {
        var address = new AudioObjectPropertyAddress(PropertyMute, ScopeInput);
        var value = muted ? 1u : 0u;
        SetPropertyData(deviceId, address, value);
    }

    internal static bool CanControlVolume(uint deviceId, int channel)
    {
        var address = new AudioObjectPropertyAddress(PropertyVolumeScalar, ScopeInput, (uint)channel);
        return AudioObjectHasProperty(deviceId, ref address) &&
               AudioObjectIsPropertySettable(deviceId, ref address, out var settable) == 0 && settable;
    }

    internal static float GetVolume(uint deviceId, int channel)
    {
        var address = new AudioObjectPropertyAddress(PropertyVolumeScalar, ScopeInput, (uint)channel);
        var values = GetPropertyData<float>(deviceId, address);
        return values.Count > 0 ? values[0] : 1.0f;
    }

    internal static void SetVolume(uint deviceId, int channel, float volume)
    {
        var address = new AudioObjectPropertyAddress(PropertyVolumeScalar, ScopeInput, (uint)channel);
        SetPropertyData(deviceId, address, volume);
    }

    internal static bool CanControlVirtualVolume(uint deviceId, uint scope = ScopeInput)
    {
        var address = new AudioObjectPropertyAddress(PropertyVirtualMainVolume, scope);
        return AudioObjectHasProperty(deviceId, ref address) &&
               AudioObjectIsPropertySettable(deviceId, ref address, out var settable) == 0 && settable;
    }

    internal static float GetVirtualVolume(uint deviceId, uint scope = ScopeInput)
    {
        var address = new AudioObjectPropertyAddress(PropertyVirtualMainVolume, scope);
        var values = GetPropertyData<float>(deviceId, address);
        return values.Count > 0 ? values[0] : 1.0f;
    }

    internal static void SetVirtualVolume(uint deviceId, float volume, uint scope = ScopeInput)
    {
        var address = new AudioObjectPropertyAddress(PropertyVirtualMainVolume, scope);
        SetPropertyData(deviceId, address, Math.Clamp(volume, 0.0f, 1.0f));
    }

    /// <summary>
    /// Sets both the input and output virtual volume of a loopback device to
    /// unity (100%) so it passes audio through without attenuation. macOS may
    /// initialize a freshly installed virtual device at 50%, which would
    /// otherwise halve the signal on the way to downstream apps.
    /// </summary>
    internal static void SetLoopbackDeviceToFullVolume(uint deviceId)
    {
        SetVirtualVolume(deviceId, 1.0f, ScopeInput);
        SetVirtualVolume(deviceId, 1.0f, ScopeOutput);
    }

    /// <summary>
    /// Requests a smaller AUHAL slice size to reduce capture/playback latency.
    /// The hardware may clamp the value to its own supported range; a smaller
    /// slice lowers latency at the cost of more frequent callbacks.
    /// </summary>
    internal static void SetMaximumFramesPerSlice(IntPtr unit, uint maxFrames)
    {
        var size = sizeof(uint);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(ptr, (int)maxFrames);
            AudioUnitSetProperty(unit, PropertyMaximumFramesPerSlice, AudioUnitScopeGlobal, 0, ptr, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    internal static unsafe IntPtr CreateInputUnit(
        uint deviceId,
        int channels,
        double sampleRate,
        AudioRenderCallback callback,
        IntPtr refCon,
        out AudioStreamBasicDescription format,
        int bufferFrameSize = 0)
    {
        var description = new AudioComponentDescription
        {
            ComponentType = AudioUnitTypeOutput,
            ComponentSubType = AudioUnitSubTypeHalOutput,
            ComponentManufacturer = AudioUnitManufacturerApple,
            ComponentFlags = 0,
            ComponentFlagsMask = 0
        };
        var component = AudioComponentFindNext(IntPtr.Zero, ref description);
        if (component == IntPtr.Zero)
        {
            format = default;
            return IntPtr.Zero;
        }

        if (AudioComponentInstanceNew(component, out var unit) != 0)
        {
            format = default;
            return IntPtr.Zero;
        }

        format = CreateCanonicalFormat(sampleRate, channels);
        uint formatSize = (uint)sizeof(AudioStreamBasicDescription);
        var formatPtr = Marshal.AllocHGlobal((int)formatSize);
        try
        {
            Marshal.StructureToPtr(format, formatPtr, false);

            var enable = 1u;
            AudioUnitSetProperty(unit, PropertyEnableIo, AudioUnitScopeInput, 1, (IntPtr)(&enable), sizeof(uint));
            var disable = 0u;
            AudioUnitSetProperty(unit, PropertyEnableIo, AudioUnitScopeOutput, 0, (IntPtr)(&disable), sizeof(uint));

            var currentDevice = deviceId;
            AudioUnitSetProperty(unit, PropertyCurrentDevice, AudioUnitScopeGlobal, 0, (IntPtr)(&currentDevice), sizeof(uint));

            AudioUnitSetProperty(unit, PropertyStreamFormat, AudioUnitScopeOutput, 1, formatPtr, formatSize);

            var callbackStruct = new AudioRenderCallbackStruct
            {
                InputProc = Marshal.GetFunctionPointerForDelegate(callback),
                InputProcRefCon = refCon
            };
            var callbackPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AudioRenderCallbackStruct>());
            try
            {
                Marshal.StructureToPtr(callbackStruct, callbackPtr, false);
                AudioUnitSetProperty(unit, PropertySetInputCallback, AudioUnitScopeGlobal, 0, callbackPtr, (uint)Marshal.SizeOf<AudioRenderCallbackStruct>());
            }
            finally
            {
                Marshal.FreeHGlobal(callbackPtr);
            }

            if (bufferFrameSize > 0)
            {
                SetMaximumFramesPerSlice(unit, (uint)bufferFrameSize);
            }

            if (AudioUnitInitialize(unit) != 0)
            {
                if (bufferFrameSize > 0)
                {
                    // The requested slice size is unsupported by the device;
                    // fall back to a conservative size so the monitor still runs.
                    AudioUnitUninitialize(unit);
                    SetMaximumFramesPerSlice(unit, 4096);
                    if (AudioUnitInitialize(unit) != 0)
                    {
                        AudioComponentInstanceDispose(unit);
                        format = default;
                        return IntPtr.Zero;
                    }
                }
                else
                {
                    AudioComponentInstanceDispose(unit);
                    format = default;
                    return IntPtr.Zero;
                }
            }

            return unit;
        }
        finally
        {
            Marshal.FreeHGlobal(formatPtr);
        }
    }

    internal static unsafe IntPtr CreateOutputUnit(
        uint deviceId,
        AudioStreamBasicDescription format,
        AudioRenderCallback callback,
        IntPtr refCon)
    {
        var description = new AudioComponentDescription
        {
            ComponentType = AudioUnitTypeOutput,
            ComponentSubType = AudioUnitSubTypeDefaultOutput,
            ComponentManufacturer = AudioUnitManufacturerApple,
            ComponentFlags = 0,
            ComponentFlagsMask = 0
        };
        var component = AudioComponentFindNext(IntPtr.Zero, ref description);
        if (component == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (AudioComponentInstanceNew(component, out var unit) != 0)
        {
            return IntPtr.Zero;
        }

        var formatPtr = Marshal.AllocHGlobal(sizeof(AudioStreamBasicDescription));
        try
        {
            Marshal.StructureToPtr(format, formatPtr, false);
            AudioUnitSetProperty(unit, PropertyStreamFormat, AudioUnitScopeInput, 0, formatPtr, (uint)sizeof(AudioStreamBasicDescription));

            var callbackStruct = new AudioRenderCallbackStruct
            {
                InputProc = Marshal.GetFunctionPointerForDelegate(callback),
                InputProcRefCon = refCon
            };
            var callbackPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AudioRenderCallbackStruct>());
            try
            {
                Marshal.StructureToPtr(callbackStruct, callbackPtr, false);
                AudioUnitSetProperty(unit, PropertySetRenderCallback, AudioUnitScopeInput, 0, callbackPtr, (uint)Marshal.SizeOf<AudioRenderCallbackStruct>());
            }
            finally
            {
                Marshal.FreeHGlobal(callbackPtr);
            }

            if (AudioUnitInitialize(unit) != 0)
            {
                AudioComponentInstanceDispose(unit);
                return IntPtr.Zero;
            }

            return unit;
        }
        finally
        {
            Marshal.FreeHGlobal(formatPtr);
        }
    }

    // Creates an AUHAL output unit bound to a specific device. Unlike
    // CreateOutputUnit, this does not route through the default output device.
    internal static unsafe IntPtr CreateOutputUnitForDevice(
        uint deviceId,
        AudioStreamBasicDescription format,
        AudioRenderCallback callback,
        IntPtr refCon,
        int bufferFrameSize = 0)
    {
        var description = new AudioComponentDescription
        {
            ComponentType = AudioUnitTypeOutput,
            ComponentSubType = AudioUnitSubTypeHalOutput,
            ComponentManufacturer = AudioUnitManufacturerApple,
            ComponentFlags = 0,
            ComponentFlagsMask = 0
        };
        var component = AudioComponentFindNext(IntPtr.Zero, ref description);
        if (component == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (AudioComponentInstanceNew(component, out var unit) != 0)
        {
            return IntPtr.Zero;
        }

        var formatPtr = Marshal.AllocHGlobal(sizeof(AudioStreamBasicDescription));
        try
        {
            Marshal.StructureToPtr(format, formatPtr, false);

            var disable = 0u;
            AudioUnitSetProperty(unit, PropertyEnableIo, AudioUnitScopeInput, 1, (IntPtr)(&disable), sizeof(uint));
            var enable = 1u;
            AudioUnitSetProperty(unit, PropertyEnableIo, AudioUnitScopeOutput, 0, (IntPtr)(&enable), sizeof(uint));

            var currentDevice = deviceId;
            AudioUnitSetProperty(unit, PropertyCurrentDevice, AudioUnitScopeGlobal, 0, (IntPtr)(&currentDevice), sizeof(uint));

            AudioUnitSetProperty(unit, PropertyStreamFormat, AudioUnitScopeInput, 0, formatPtr, (uint)sizeof(AudioStreamBasicDescription));

            var callbackStruct = new AudioRenderCallbackStruct
            {
                InputProc = Marshal.GetFunctionPointerForDelegate(callback),
                InputProcRefCon = refCon
            };
            var callbackPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AudioRenderCallbackStruct>());
            try
            {
                Marshal.StructureToPtr(callbackStruct, callbackPtr, false);
                AudioUnitSetProperty(unit, PropertySetRenderCallback, AudioUnitScopeInput, 0, callbackPtr, (uint)Marshal.SizeOf<AudioRenderCallbackStruct>());
            }
            finally
            {
                Marshal.FreeHGlobal(callbackPtr);
            }

            if (bufferFrameSize > 0)
            {
                SetMaximumFramesPerSlice(unit, (uint)bufferFrameSize);
            }

            if (AudioUnitInitialize(unit) != 0)
            {
                if (bufferFrameSize > 0)
                {
                    // Unsupported slice size → fall back to a conservative size.
                    AudioUnitUninitialize(unit);
                    SetMaximumFramesPerSlice(unit, 4096);
                    if (AudioUnitInitialize(unit) != 0)
                    {
                        AudioComponentInstanceDispose(unit);
                        return IntPtr.Zero;
                    }
                }
                else
                {
                    AudioComponentInstanceDispose(unit);
                    return IntPtr.Zero;
                }
            }

            return unit;
        }
        finally
        {
            Marshal.FreeHGlobal(formatPtr);
        }
    }

    internal static int StartUnit(IntPtr unit) => AudioOutputUnitStart(unit);

    internal static int StopUnit(IntPtr unit) => AudioOutputUnitStop(unit);

    internal static void DisposeUnit(IntPtr unit)
    {
        if (unit == IntPtr.Zero)
        {
            return;
        }

        AudioOutputUnitStop(unit);
        AudioUnitUninitialize(unit);
        AudioComponentInstanceDispose(unit);
    }

    internal static AudioStreamBasicDescription CreateCanonicalFormat(double sampleRate, int channels)
    {
        channels = Math.Clamp(channels, 1, 8);
        var bytesPerFrame = (uint)(channels * sizeof(float));
        return new AudioStreamBasicDescription
        {
            SampleRate = sampleRate,
            FormatId = AudioFormatLinearPcm,
            FormatFlags = AudioFormatFlagIsFloat | AudioFormatFlagIsPacked,
            BytesPerPacket = bytesPerFrame,
            FramesPerPacket = 1,
            BytesPerFrame = bytesPerFrame,
            ChannelsPerFrame = (uint)channels,
            BitsPerChannel = 32,
            Reserved = 0
        };
    }

    internal static AudioStreamBasicDescription CreatePcmFormat(double sampleRate, int channels, int bitsPerSample)
    {
        channels = Math.Clamp(channels, 1, 8);
        var bytesPerSample = (bitsPerSample + 7) / 8;
        var bytesPerFrame = (uint)(channels * bytesPerSample);
        return new AudioStreamBasicDescription
        {
            SampleRate = sampleRate,
            FormatId = AudioFormatLinearPcm,
            FormatFlags = AudioFormatFlagIsSignedInteger | AudioFormatFlagIsPacked,
            BytesPerPacket = bytesPerFrame,
            FramesPerPacket = 1,
            BytesPerFrame = bytesPerFrame,
            ChannelsPerFrame = (uint)channels,
            BitsPerChannel = (uint)bitsPerSample,
            Reserved = 0
        };
    }

    internal static AudioFormat ToAudioFormat(AudioStreamBasicDescription format) =>
        new(
            (int)Math.Round(format.SampleRate),
            (int)format.BitsPerChannel,
            (int)format.ChannelsPerFrame,
            (format.FormatFlags & AudioFormatFlagIsFloat) != 0
                ? AudioSampleEncoding.IeeeFloat
                : AudioSampleEncoding.Pcm);

    private static List<T> GetPropertyData<T>(uint objectId, AudioObjectPropertyAddress address) where T : unmanaged
    {
        var bytes = GetPropertyDataRaw(objectId, address);
        var size = Marshal.SizeOf<T>();
        var count = bytes.Length / size;
        var result = new List<T>(count);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var pointer = handle.AddrOfPinnedObject();
            for (var index = 0; index < count; index++)
            {
                result.Add(Marshal.PtrToStructure<T>(IntPtr.Add(pointer, index * size)));
            }
        }
        finally
        {
            handle.Free();
        }

        return result;
    }

    private static byte[] GetPropertyDataRaw(uint objectId, AudioObjectPropertyAddress address)
    {
        var dataSize = 0u;
        if (AudioObjectGetPropertyDataSize(objectId, ref address, 0, IntPtr.Zero, ref dataSize) != 0 || dataSize == 0)
        {
            return [];
        }

        var data = Marshal.AllocHGlobal((int)dataSize);
        try
        {
            if (AudioObjectGetPropertyData(objectId, ref address, 0, IntPtr.Zero, ref dataSize, data) != 0)
            {
                return [];
            }

            var bytes = new byte[dataSize];
            Marshal.Copy(data, bytes, 0, (int)dataSize);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private static string GetStringProperty(uint objectId, AudioObjectPropertyAddress address)
    {
        var bytes = GetPropertyDataRaw(objectId, address);
        if (bytes.Length < IntPtr.Size)
        {
            return string.Empty;
        }

        var cfString = IntPtr.Size == 8
            ? (IntPtr)BitConverter.ToInt64(bytes, 0)
            : (IntPtr)BitConverter.ToInt32(bytes, 0);
        return CfStringToString(cfString);
    }

    private static void SetPropertyData<T>(uint objectId, AudioObjectPropertyAddress address, T value) where T : unmanaged
    {
        var data = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try
        {
            Marshal.StructureToPtr(value, data, false);
            AudioObjectSetPropertyData(objectId, ref address, 0, IntPtr.Zero, (uint)Marshal.SizeOf<T>(), data);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private static string CfStringToString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero)
        {
            return string.Empty;
        }

        var length = CFStringGetLength(cfString);
        if (length <= 0)
        {
            return string.Empty;
        }

        var bufferSize = checked((int)(length * 4 + 1));
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (!CFStringGetCString(cfString, buffer, bufferSize, KcfStringEncodingUtf8))
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUTF8(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
