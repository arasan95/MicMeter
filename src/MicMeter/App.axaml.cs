using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MicMeter.Services;

namespace MicMeter;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _singleInstance = new SingleInstanceGuard("MicMeter.SingleInstance");
            if (!_singleInstance.IsOwner)
            {
                desktop.Shutdown();
                return;
            }

            desktop.Exit += (_, _) => _singleInstance?.Dispose();
            var settingsStore = new SettingsStore();
            var settings = settingsStore.Load();
            desktop.MainWindow = new MainWindow(settingsStore, settings);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
