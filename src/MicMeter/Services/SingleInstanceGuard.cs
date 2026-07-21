using System.Threading;

namespace MicMeter.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(true, $"Local\\{name}", out var createdNew);
        IsOwner = createdNew;
    }

    public bool IsOwner { get; }

    public void Dispose()
    {
        if (IsOwner)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}

