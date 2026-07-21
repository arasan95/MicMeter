using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicMeter.Models;

public sealed class DeviceSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public DeviceSelectionItem(AudioDeviceInfo device, bool isSelected)
    {
        Id = device.Id;
        Name = device.Name;
        _isSelected = isSelected;
    }

    public string Id { get; }
    public string Name { get; }

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

    public event PropertyChangedEventHandler? PropertyChanged;
}

