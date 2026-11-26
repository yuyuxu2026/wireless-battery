using System.Windows;
using WirelessBattery.Ui;

namespace WirelessBattery;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _instance = new Mutex(true, @"Local\Yu.WirelessBattery", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _tray = new TrayController();
        _tray.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
