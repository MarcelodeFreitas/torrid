using System.Runtime.InteropServices;
using System.Windows;
using Torrid.App.Notifications;

namespace Torrid.App;

public partial class App : System.Windows.Application
{
    // --test-toast timings: Windows needs a while to index a brand-new Start Menu shortcut before it accepts toasts
    // for its AppUserModelID; the second wait gives the toast time to be recorded before the process exits.
    private static readonly TimeSpan TestToastDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TestToastLinger = TimeSpan.FromSeconds(4);

    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Identity first, then the shortcut Windows needs before it delivers this app's toasts.
        var hr = SetCurrentProcessExplicitAppUserModelID(AppIdentity.Aumid);
        if (hr != 0) System.Diagnostics.Debug.WriteLine($"SetCurrentProcessExplicitAppUserModelID failed: 0x{hr:X8}");
        StartMenuShortcut.EnsureForToasts();
        base.OnStartup(e);

        var options = StartupOptions.Parse(e.Args);
        _host = new AppHost(options);

        var mainWindow = new MainWindow { DataContext = _host.Shell };
        MainWindow = mainWindow;
        _host.AttachWindow(mainWindow);

        if (options.TestToast)
        {
            // Sensors are deliberately not started: a real red alert would also change Windows' "notification
            // accepted" signal that tools/Verify-Toast.ps1 checks, and could make it pass for the wrong reason.
            RunToastSelfTest(_host);
            return;
        }

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

    private static async void RunToastSelfTest(AppHost host)
    {
        await Task.Delay(TestToastDelay);
        host.SendTestNotification();
        await Task.Delay(TestToastLinger);
        host.Exit();
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
}
