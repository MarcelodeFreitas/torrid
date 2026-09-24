namespace Torrid.App;

using System.Windows;
using Torrid.App.Sensors;
using Torrid.App.ViewModels;
using Torrid.Core.Config;

public partial class MainWindow : Window
{
    private readonly FakeSensorSource _sensorSource = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(_sensorSource, new AppConfig());
        _sensorSource.Start();
        Closed += (_, _) => _sensorSource.Stop();
    }
}
