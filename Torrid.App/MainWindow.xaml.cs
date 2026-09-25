namespace Torrid.App;

using System.Windows;
using Torrid.App.Notifications;
using Torrid.App.Tray;
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Notifications;
using Torrid.Core.Sensors;
using Torrid.Core.Time;

public partial class MainWindow : Window
{
    private readonly IConfigStore _configStore = new JsonConfigStore();
    private readonly AppConfig _config;
    private readonly SensorService _sensorSource;
    private readonly NotificationService _notificationService;
    private readonly TrayIconGroupManager _trayIconGroupManager;

    public MainWindow()
    {
        InitializeComponent();

        _config = _configStore.Load();
        _sensorSource = new SensorService(_config.PollIntervalMs);
        _notificationService = new NotificationService(new WindowsToastNotifier(), new SystemClock(), _config.NotificationRefireMinutes);

        DataContext = new MainWindowViewModel(_sensorSource, _config);

        _trayIconGroupManager = new TrayIconGroupManager(_sensorSource, _config);
        _trayIconGroupManager.TrayIconClicked += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        MouseRightButtonUp += (_, _) => OpenSettings();

        _sensorSource.ReadingsUpdated += OnReadingsUpdatedForNotifications;

        _sensorSource.Start();

        Closed += (_, _) =>
        {
            _sensorSource.Stop();
            _sensorSource.Dispose();
            _trayIconGroupManager.Dispose();
        };
    }

    private void OnReadingsUpdatedForNotifications(object? sender, IReadOnlyList<Core.Models.SensorReading> readings)
    {
        var notifiable = readings.Where(r => Core.Config.SensorDisplay.NotificationsEnabled(_config, r.Id)).ToList();
        _notificationService.Evaluate(notifiable, _config.Thresholds);
    }

    private void OpenSettings()
    {
        var settings = new SettingsWindow(_config, _sensorSource.CurrentReadings, _configStore, newPollIntervalMs => _sensorSource.PollIntervalMs = newPollIntervalMs)
        {
            Owner = this,
        };
        settings.ShowDialog();
    }
}
