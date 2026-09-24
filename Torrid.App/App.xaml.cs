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
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        base.OnStartup(e);
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
}

