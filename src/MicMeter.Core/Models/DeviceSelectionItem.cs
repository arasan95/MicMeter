using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicMeter.Models;

public sealed class DeviceSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isLoopback;

    public DeviceSelectionItem(AudioDeviceInfo device, bool isSelected, bool isLoopback = false)
    {
        Id = device.Id;
        Name = device.Name;
        _isSelected = isSelected;
        _isLoopback = isLoopback;
    }

    public string Id { get; }
    public string Name { get; }

    /// <summary>
    /// Whether the loopback-routing toggle should be shown. Loopback routing is
    /// macOS-only (the MicMeter virtual audio device exists only there), so this
    /// is false on Windows and hides the related UI.
    /// </summary>
    public bool IsLoopbackVisible => OperatingSystem.IsMacOS();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public bool IsLoopback
    {
        get => _isLoopback;
        set
        {
            if (_isLoopback == value) return;
            _isLoopback = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoopback)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

