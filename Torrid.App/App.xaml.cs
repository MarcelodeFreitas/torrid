using System.Configuration;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;

namespace Torrid.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string AppUserModelId = "Torrid.App";

    protected override void OnStartup(StartupEventArgs e)
    {
        var hr = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        if (hr != 0) System.Diagnostics.Debug.WriteLine($"SetCurrentProcessExplicitAppUserModelID failed: 0x{hr:X8}");
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        if (!mainWindow.StartMinimizedToTray || e.Args.Contains("--show"))
        {
            mainWindow.Show();
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
}
