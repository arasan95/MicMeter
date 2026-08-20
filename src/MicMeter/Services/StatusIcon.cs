namespace MicMeter.Services;

public sealed class StatusIconActions
{
    public required Action ShowHide { get; init; }
    public required Action ToggleMute { get; init; }
    public required Action Settings { get; init; }
    public required Action Quit { get; init; }
}

public interface IStatusIcon : IDisposable
{
    void SetIcon(byte[] pngBytes);
    void SetToolTip(string text);
    void UpdateLanguage();
    event EventHandler? LeftClicked;
}
