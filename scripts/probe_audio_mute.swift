// probe_audio_mute.swift
// Investigates CoreAudio input-device mute capabilities for all devices.
// Checks, for the INPUT scope of each audio device:
//   1. Hardware mute (kAudioDevicePropertyMute) — has? settable?
//   2. Virtual main volume (kAudioHardwareServiceDeviceProperty_VirtualMainVolume) — has? settable? current value
//   3. Per-channel volume scalar (kAudioDevicePropertyVolumeScalar) — has? settable? current value
//   4. Current hardware mute state (read-only)
//
// Usage: swift probe_audio_mute.swift

import CoreAudio
import Foundation

// kAudioHardwareServiceDeviceProperty_VirtualMainVolume is not exposed to Swift directly.
// Value: 'vmvc' (0x766D7663)
private let kAudioHardwareServiceDeviceProperty_VirtualMainVolume: AudioObjectPropertySelector = 0x766D7663

func checkProperty(_ deviceID: AudioDeviceID, _ selector: AudioObjectPropertySelector, _ scope: AudioObjectPropertyScope, _ element: AudioObjectPropertyElement) -> (has: Bool, settable: Bool, canGet: Bool) {
    var address = AudioObjectPropertyAddress(
        mSelector: selector,
        mScope: scope,
        mElement: element
    )
    let has = AudioObjectHasProperty(deviceID, &address)
    var settable = false
    var isSettable: DarwinBoolean = false
    var settableErr: OSStatus = -1
    if has {
        settableErr = AudioObjectIsPropertySettable(deviceID, &address, &isSettable)
        settable = settableErr == noErr && isSettable.boolValue
    }
    // canGet: does a read succeed?
    var canGet = false
    if has {
        var value: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        let err = AudioObjectGetPropertyData(deviceID, &address, 0, nil, &size, &value)
        canGet = err == noErr
    }
    return (has, settable, canGet)
}

func readUInt32(_ deviceID: AudioDeviceID, _ selector: AudioObjectPropertySelector, _ scope: AudioObjectPropertyScope, _ element: AudioObjectPropertyElement) -> UInt32? {
    var address = AudioObjectPropertyAddress(
        mSelector: selector,
        mScope: scope,
        mElement: element
    )
    guard AudioObjectHasProperty(deviceID, &address) else { return nil }
    var value: UInt32 = 0
    var size = UInt32(MemoryLayout<UInt32>.size)
    let err = AudioObjectGetPropertyData(deviceID, &address, 0, nil, &size, &value)
    return err == noErr ? value : nil
}

func readFloat(_ deviceID: AudioDeviceID, _ selector: AudioObjectPropertySelector, _ scope: AudioObjectPropertyScope, _ element: AudioObjectPropertyElement) -> Float? {
    var address = AudioObjectPropertyAddress(
        mSelector: selector,
        mScope: scope,
        mElement: element
    )
    guard AudioObjectHasProperty(deviceID, &address) else { return nil }
    var value: Float = 0
    var size = UInt32(MemoryLayout<Float>.size)
    let err = AudioObjectGetPropertyData(deviceID, &address, 0, nil, &size, &value)
    return err == noErr ? value : nil
}

func deviceName(_ deviceID: AudioDeviceID) -> String {
    var address = AudioObjectPropertyAddress(
        mSelector: kAudioObjectPropertyName,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    var name: CFString = "" as CFString
    var size = UInt32(MemoryLayout<CFString>.size)
    let err = AudioObjectGetPropertyData(deviceID, &address, 0, nil, &size, &name)
    return err == noErr ? (name as String) : "(unnamed)"
}

func deviceUID(_ deviceID: AudioDeviceID) -> String {
    var address = AudioObjectPropertyAddress(
        mSelector: kAudioDevicePropertyDeviceUID,
        mScope: kAudioObjectPropertyScopeGlobal,
        mElement: kAudioObjectPropertyElementMain
    )
    var uid: CFString = "" as CFString
    var size = UInt32(MemoryLayout<CFString>.size)
    let err = AudioObjectGetPropertyData(deviceID, &address, 0, nil, &size, &uid)
    return err == noErr ? (uid as String) : "(none)"
}

func inputChannelCount(_ deviceID: AudioDeviceID) -> Int {
    var address = AudioObjectPropertyAddress(
        mSelector: kAudioDevicePropertyStreamConfiguration,
        mScope: kAudioObjectPropertyScopeInput,
        mElement: kAudioObjectPropertyElementMain
    )
    var size: UInt32 = 0
    guard AudioObjectGetPropertyDataSize(deviceID, &address, 0, nil, &size) == noErr else { return 0 }
    let bufferList = UnsafeMutableRawPointer.allocate(byteCount: Int(size), alignment: MemoryLayout<AudioBufferList>.alignment)
    defer { bufferList.deallocate() }
    guard AudioObjectGetPropertyData(deviceID, &address, 0, nil, &size, bufferList) == noErr else { return 0 }
    let abl = bufferList.assumingMemoryBound(to: AudioBufferList.self)
    var count = 0
    for i in 0..<Int(abl.pointee.mNumberBuffers) {
        let buf = AudioBuffer.self // ignore
        _ = buf
        // Access via UnsafeMutableAudioBufferListPointer
    }
    return count
}

// --- Enumerate all devices ---
var address = AudioObjectPropertyAddress(
    mSelector: kAudioHardwarePropertyDevices,
    mScope: kAudioObjectPropertyScopeGlobal,
    mElement: kAudioObjectPropertyElementMain
)
var dataSize: UInt32 = 0
AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &dataSize)
let deviceCount = Int(dataSize) / MemoryLayout<AudioDeviceID>.size
var devices = [AudioDeviceID](repeating: 0, count: deviceCount)
AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &dataSize, &devices)

print("=== Audio device INPUT-SCOPE capability probe ===")
print("")

let scopeInput = kAudioObjectPropertyScopeInput

for device in devices {
    let name = deviceName(device)
    let uid = deviceUID(device)
    var inputAddr = AudioObjectPropertyAddress(
        mSelector: kAudioDevicePropertyStreamConfiguration,
        mScope: scopeInput,
        mElement: kAudioObjectPropertyElementMain
    )
    let hasInput = AudioObjectHasProperty(device, &inputAddr)

    print("Device: \(name)")
    print("  UID: \(uid)")

    // Hardware mute (input scope)
    let mute = checkProperty(device, kAudioDevicePropertyMute, scopeInput, kAudioObjectPropertyElementMain)
    let muteState = readUInt32(device, kAudioDevicePropertyMute, scopeInput, kAudioObjectPropertyElementMain)
    print("  [1] Hardware mute  (kAudioDevicePropertyMute, input):")
    print("      has=\(mute.has) settable=\(mute.settable) readable=\(mute.canGet) currentState=\(muteState.map(String.init) ?? "n/a")")

    // Virtual main volume (input scope)
    let virtVol = checkProperty(device, kAudioHardwareServiceDeviceProperty_VirtualMainVolume, scopeInput, kAudioObjectPropertyElementMain)
    let virtVolVal = readFloat(device, kAudioHardwareServiceDeviceProperty_VirtualMainVolume, scopeInput, kAudioObjectPropertyElementMain)
    print("  [2] Virtual main volume (kAudioHardwareServiceDeviceProperty_VirtualMainVolume, input):")
    print("      has=\(virtVol.has) settable=\(virtVol.settable) readable=\(virtVol.canGet) currentValue=\(virtVolVal.map { String(format: "%.3f", $0) } ?? "n/a")")

    // Per-channel volume scalar (element 0 = main / master)
    let vol0 = checkProperty(device, kAudioDevicePropertyVolumeScalar, scopeInput, kAudioObjectPropertyElementMain)
    let vol0Val = readFloat(device, kAudioDevicePropertyVolumeScalar, scopeInput, kAudioObjectPropertyElementMain)
    print("  [3] Volume scalar ch(main) (kAudioDevicePropertyVolumeScalar, input, element main):")
    print("      has=\(vol0.has) settable=\(vol0.settable) readable=\(vol0.canGet) currentValue=\(vol0Val.map { String(format: "%.3f", $0) } ?? "n/a")")

    // Channel 1 volume scalar
    let vol1 = checkProperty(device, kAudioDevicePropertyVolumeScalar, scopeInput, 1)
    let vol1Val = readFloat(device, kAudioDevicePropertyVolumeScalar, scopeInput, 1)
    print("  [4] Volume scalar ch1 (kAudioDevicePropertyVolumeScalar, input, element 1):")
    print("      has=\(vol1.has) settable=\(vol1.settable) readable=\(vol1.canGet) currentValue=\(vol1Val.map { String(format: "%.3f", $0) } ?? "n/a")")

    print("")
}