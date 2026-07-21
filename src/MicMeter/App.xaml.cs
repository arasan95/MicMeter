using System.Windows;
using MicMeter.Services;

namespace MicMeter;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceGuard("MicMeter.SingleInstance");
        if (!_singleInstance.IsOwner)
        {
            Shutdown();
            return;
        }

        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        var window = new MainWindow(settingsStore, settings);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
