namespace Torrid.App;

using System.Windows;
using Torrid.App.Sensors;
using Torrid.App.Tray;
using Torrid.App.ViewModels;
using Torrid.Core.Config;

public partial class MainWindow : Window
{
    private readonly FakeSensorSource _sensorSource = new();
    private readonly TrayIconManager _trayIconManager;

    public MainWindow()
    {
        InitializeComponent();
        var config = new AppConfig();
        DataContext = new MainWindowViewModel(_sensorSource, config);
        _trayIconManager = new TrayIconManager(_sensorSource, config);
        _trayIconManager.TrayIconClicked += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        _sensorSource.Start();
        Closed += (_, _) => { _sensorSource.Stop(); _trayIconManager.Dispose(); };
    }
}
