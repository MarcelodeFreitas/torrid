using System.Runtime.InteropServices;
using System.Windows;

namespace Torrid.App;

public partial class App : System.Windows.Application
{
    private const string AppUserModelId = "Torrid.App";

    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        var hr = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        if (hr != 0) System.Diagnostics.Debug.WriteLine($"SetCurrentProcessExplicitAppUserModelID failed: 0x{hr:X8}");
        base.OnStartup(e);

        var options = StartupOptions.Parse(e.Args);
        _host = new AppHost(options);

        var mainWindow = new MainWindow { DataContext = _host.Shell };
        MainWindow = mainWindow;
        _host.AttachWindow(mainWindow);
        _host.Start();

        if (options.ShowWindow || !_host.ConfigManager.Config.StartMinimizedToTray)
        {
            _host.ShowMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
}
